using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hi
{
    public abstract class HiBase
    {
        internal readonly Side Side;

        private int _uk;

        private readonly object _syncRoot = new object();

        private ConcurrentQueue<Request>           _msgs = new ConcurrentQueue<Request>();
        private ConcurrentDictionary<int, Request> _busy = new ConcurrentDictionary<int, Request>();
        private ConcurrentQueue<Request>           _done = new ConcurrentQueue<Request>();
        private ConcurrentQueue<DelayedReply>      _repl = new ConcurrentQueue<DelayedReply>();

        public Action<string>       Log;
        public Func<string, string> Receive;
        public bool                 IsConnected { get; protected set; }

        private DateTimeOffset _lastHeartbeat;
        private int            _heartbeatId = -5;

        private ConcurrentBag<Thread> _threads = new ConcurrentBag<Thread>();

        protected volatile bool Stopped;

        internal HiBase(Side side)
        {
            Side = side;
        }

        protected int GetPort(string name)
        {
            int    hash   = name.GetHashCode();
            ushort hash16 = (ushort) ((hash >> 16) ^ hash);
            if(hash16 < 1024) hash16 += 1024;
            return hash16;
        }

        private protected void ListenTcpStreams(TcpClient client)
        {
            LogMsg("starting tcp");
            _lastHeartbeat = DateTimeOffset.Now;
            var stream = client.GetStream();
            try
            {
                while(client.Connected)
                {
                    if(Stopped) return;
                    Heartbeat();

                    bool wait = true;
                    while(_msgs.TryDequeue(out var request))
                    {
                        if(Stopped) return;

                        if(request.FromSide == Side) _busy.TryAdd(request.Id, request);

                        WriteInt(stream, (int)request.FromSide);
                        WriteInt(stream, request.Id);
                        WriteString(stream, request.Data.Msg);

                        if(_heartbeatId != request.Id)
                        {
                            LogMsg($"sent {request.Data.Msg}");
                        }

                        wait = false;
                    }

                    while(stream.DataAvailable)
                    {
                        if(Stopped) return;

                        var fromSide = (Side)ReadInt(stream);
                        int id = ReadInt(stream);
                        var data = ReadString(stream);

                        if(id == _heartbeatId) continue;

                        LogMsg($"read {data}");

                        if(fromSide == Side)
                        {
                            if(_busy.TryRemove(id, out var request))
                            {
                                Complete(request, null, data);
                            }
                        }
                        else
                        {
                            ReceiveInvoke(data, id, fromSide);
                        }

                        wait = false;
                    }

                    if(wait)
                    {
                        if(Stopped) return;
                        Thread.Sleep(HiConst.SendDelay);
                    }
                }
            }
            catch(Exception exception) when(exception is SocketException || exception.InnerException is SocketException)
            {
                LogMsg("Tcp disconnected");
            }
            catch(ThreadAbortException exception)
            {
                LogMsg("Tcp aborted");
            }
            catch(ThreadInterruptedException exception)
            {
                LogMsg("Tcp interrupted");
            }
            catch(Exception exception)
            {
                LogMsg(exception.ToString());
                LogMsg("Tcp disconnected due to exception");
            }
            finally
            {
                stream.Close();
                client.Close();
                var canceled = _busy.ToList();
                _busy.Clear();
                foreach(var pair in canceled)
                {
                    Complete(pair.Value, "disconnected", null);
                }

                IsConnected = false;
            }
        }

        private void ReceiveInvoke(string data, int id, Side fromSide)
        {
            if(ManualMessagePolling)
            {
                _repl.Enqueue(new DelayedReply(data, id, fromSide));
            }
            else
            {
                var response = Receive?.Invoke(data);
                EnqueueMsg(new Request(response, id, fromSide));
            }
        }

        protected void LogMsg(string msg)
        {
            Log?.Invoke($"[{Side}] {msg}");
        }

        private void EnqueueMsg(Request request)
        {
            _msgs.Enqueue(request);
        }

        private void Heartbeat()
        {
            if(DateTimeOffset.Now - _lastHeartbeat > TimeSpan.FromMilliseconds(HiConst.WatchPeriod))
            {
                _lastHeartbeat = DateTimeOffset.Now;
                EnqueueMsg(new Request(null, _heartbeatId, Side));
            }
        }

        protected void StartThread(Action action)
        {
            //ThreadPool.QueueUserWorkItem(p => action());
            var threadStart = new ThreadStart(action);
            var thread = new Thread(threadStart);
            thread.Start();
            _threads.Add(thread);
        }

        public bool ManualMessagePolling { get; set; }

        public void PollMessages()
        {
            while(_done.TryDequeue(out var request))
            {
                if(Stopped) return;

                request.Continue();
            }

            while(_repl.TryDequeue(out var  reply))
            {
                if(Stopped) return;
                var response = Receive?.Invoke(reply.Data);
                EnqueueMsg(new Request(response, reply.Id, reply.FromSide));
            }
        }

        private void Complete(Request request, string error, string data)
        {
            request.Complete(error, data);

            if(ManualMessagePolling)
            {
                _done.Enqueue(request);
            }
            else
            {
                var thread = new Thread(() => request.Continue());
                thread.Start();
            }
        }

        protected int ReadInt(NetworkStream stream)
        {
            byte[] buffer = new byte[4];
            stream.Read(buffer, 0, buffer.Length);
            int value = BitConverter.ToInt32(buffer, 0);
            return value;
        }

        protected string ReadString(NetworkStream stream)
        {
            int bufferSize = ReadInt(stream);
            if(bufferSize == -1) return null;
            if(bufferSize == 0) return "";

            byte[] buffer = new byte[bufferSize];
            stream.Read(buffer, 0, buffer.Length);
            var value = Encoding.UTF8.GetString(buffer);
            return value;
        }

        protected void WriteInt(NetworkStream stream, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            stream.Write(buffer, 0, buffer.Length);
        }

        protected void WriteString(NetworkStream stream, string str)
        {
            if(str == null)
            {
                WriteInt(stream, -1);
                return;
            }

            if(str == "")
            {
                WriteInt(stream, 0);
                return;
            }

            var buffer = Encoding.UTF8.GetBytes(str);
            WriteInt(stream, buffer.Length);
            stream.Write(buffer, 0, buffer.Length);
        }

        public async Task<ResponseData> Send(string msg)
        {
            if(!IsConnected) return new ResponseData();

            Request request;
            lock(_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                request = new Request(msg, _uk, Side);
                EnqueueMsg(request);
            }

            var result = await request;
            return result.Data;
        }

        protected void AlertStop()
        {
            Stopped = true;
            Thread.Sleep(100);
        }

        protected void Dispose()
        {
            while(_threads.Count > 0)
            {
                _threads.TryTake(out var thread);
                if(thread.IsAlive) thread.Join();
            }
            
            _threads = new ConcurrentBag<Thread>();

            _msgs = new ConcurrentQueue<Request>();
            _busy = new ConcurrentDictionary<int, Request>();
            _done = new ConcurrentQueue<Request>();

            _uk = 0;
        }
    }
}