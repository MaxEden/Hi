using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
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

        private ConcurrentQueue<Request>                 _msgs       = new();
        private ConcurrentQueue<Request>                 _done       = new();
        private ConcurrentQueue<DelayedReply>            _repl       = new();
        private ConcurrentDictionary<int, TcpClientWrap> _tcpClients = new();

        public Action<string>               Log;
        public Func<string, Sender, string> Receive;
        public bool                         IsConnected { get; protected set; }

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
            int hash = GetStableHashCode(name);
            ushort hash16 = (ushort)((hash >> 16) ^ hash);
            if(hash16 < 1024) hash16 += 1024;
            return hash16;
        }

        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for(int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if(i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        protected void NewClient(TcpClient client)
        {
            lock(_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                _tcpClients.TryAdd(_uk, new TcpClientWrap()
                {
                    Id = _uk,
                    TcpClient = client,
                    Sender = new Sender()
                });
            }

            if(_tcpClients.Count == 1)
            {
                StartThread(() => ListenTcpStreams());
            }
        }

        private void ListenTcpStreams()
        {
            LogMsg("starting tcp");

            _lastHeartbeat = DateTimeOffset.Now;
            try
            {
                while(!_tcpClients.IsEmpty)
                {
                    if(Stopped) return;
                    Heartbeat();

                    bool wait = true;


                    while(_msgs.TryDequeue(out var request))
                    {
                        if(Stopped) return;

                        foreach(var client in _tcpClients.Values)
                        {
                            if(!client.TcpClient.Connected) continue;
                            if(request.Sender != null && request.Sender != client.Sender) continue;

                            var stream = client.stream ??= client.TcpClient.GetStream();
                            if(request.FromSide == Side) client._busy.TryAdd(request.Id, request);

                            WriteInt(stream, (int)request.FromSide);
                            WriteInt(stream, request.Id);
                            WriteString(stream, request.Data.Msg);

                            if(_heartbeatId != request.Id)
                            {
                                LogMsg($"sent {request.Data.Msg}");
                            }

                            wait = false;
                        }
                    }

                    foreach(var client in _tcpClients.Values)
                    {
                        if(!client.TcpClient.Connected) continue;
                        var stream = client.stream ??= client.TcpClient.GetStream();

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
                                if(client._busy.TryRemove(id, out var request))
                                {
                                    Complete(request, null, data);
                                }
                            }
                            else
                            {
                                ReceiveInvoke(data, id, fromSide, client.Sender);
                            }

                            wait = false;
                        }
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
                foreach(var clientWrap in _tcpClients.Values)
                {
                    clientWrap.stream.Close();
                    clientWrap.TcpClient.Close();
                    var canceled = clientWrap._busy.ToList();
                    clientWrap._busy.Clear();
                    foreach(var pair in canceled)
                    {
                        Complete(pair.Value, "disconnected", null);
                    }
                }


                IsConnected = false;
            }
        }

        private void ReceiveInvoke(string data, int id, Side fromSide, Sender sender)
        {
            if(ManualMessagePolling)
            {
                _repl.Enqueue(new DelayedReply(data, id, fromSide, sender));
            }
            else
            {
                var response = Receive?.Invoke(data, sender);
                EnqueueMsg(new Request(response, id, fromSide, sender));
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
                EnqueueMsg(new Request(null, _heartbeatId, Side, null));
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

            while(_repl.TryDequeue(out var reply))
            {
                if(Stopped) return;
                var response = Receive?.Invoke(reply.Data, reply.Sender);
                EnqueueMsg(new Request(response, reply.Id, reply.FromSide, reply.Sender));
            }
        }

        private void Complete(Request request, string error, string data)
        {
            request.Complete(error, data);

            if(ManualMessagePolling && !request.Blocking)
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

        public async Task<ResponseData> Send(string msg, Sender sendTo = null)
        {
            if(!IsConnected) return new ResponseData();

            Request request;
            lock(_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                request = new Request(msg, _uk, Side, sendTo);
                EnqueueMsg(request);
            }

            var result = await request;
            return result.Data;
        }

        public ResponseData SendBlocking(string msg, int timeout = 0, Sender sendTo = null)
        {
            if(!IsConnected) return new ResponseData();

            Request request;
            lock(_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                request = new Request(msg, _uk, Side, sendTo, true);
                EnqueueMsg(request);
            }

            if(timeout == 0) timeout = HiConst.SendBlockedTimeout;
            var stopwatch = Stopwatch.StartNew();
            while(!request.IsDone)
            {
                Thread.Sleep(HiConst.SendDelay);
                if(stopwatch.ElapsedMilliseconds > timeout) return new ResponseData {Error = "timeout"};
            }
            return request.Data;
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

            _tcpClients.Clear();
            _threads = new ConcurrentBag<Thread>();
            _msgs = new ConcurrentQueue<Request>();
            _done = new ConcurrentQueue<Request>();
            _uk = 0;
        }

        private class TcpClientWrap
        {
            public TcpClient                          TcpClient;
            public Sender                             Sender;
            public int                                Id;
            public NetworkStream                      stream;
            public ConcurrentDictionary<int, Request> _busy = new();
        }
    }

    public class Sender
    {
        public string                               Name    { get; internal set; }
        public ConcurrentDictionary<string, object> DataBag { get; internal set; }

        internal Sender() {}
    }
}