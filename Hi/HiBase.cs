﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hi
{
    public abstract class HiBase
    {
        internal readonly Side Side;
        
        protected string _name;

        private int _uk;

        private readonly object _syncRoot = new object();

        private ConcurrentQueue<Request> _msgs = new();
        private ConcurrentQueue<Request> _done = new();
        private ConcurrentQueue<DelayedReply> _repl = new();
        private ConcurrentDictionary<int, TcpClientWrap> _tcpClients = new();

        public Action<string> Log;
        public Func<Msg, Sender, Msg> Receive;
        public bool IsConnected { get; protected set; }

        private DateTimeOffset _lastHeartbeat;
        private int _heartbeatId = -5;

        private ConcurrentBag<Thread> _threads = new ConcurrentBag<Thread>();

        protected volatile bool Stopped;

        private volatile Thread _tcpThread;
        private volatile AutoResetEvent _tcpWaitHandle = new AutoResetEvent(true);
        private volatile ManualResetEvent _completeHandle = new ManualResetEvent(true);

        internal HiBase(Side side)
        {
            Side = side;
        }

        protected int GetPort(string name)
        {
            int hash = GetStableHashCode(name);
            ushort hash16 = (ushort)((hash >> 16) ^ hash);
            if (hash16 < 1024) hash16 += 1024;
            return hash16;
        }

        private static int GetStableHashCode(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        protected void NewClient(TcpClient client, string clientName, string serverName)
        {
            lock (_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                var wrap = new TcpClientWrap()
                {
                    Id = _uk,
                    TcpClient = client,
                    Sender = new Sender()
                    {
                        DataBag = new ConcurrentDictionary<string, object>()
                    }
                };

                if (Side == Side.Client)
                {
                    wrap.Stream = wrap.TcpClient.GetStream();
                    Write.String(wrap.Stream, clientName);
                    wrap.Sender.Name = serverName;
                    Log("connected to server: " + serverName);
                }

                if (Side == Side.Server)
                {
                    wrap.Stream = wrap.TcpClient.GetStream();
                    clientName = Read.String(wrap.Stream);
                    wrap.Sender.Name = clientName;
                    LogMsg("client connected: " + clientName);
                }

                _tcpClients.TryAdd(_uk, wrap);
            }

            if (_tcpThread == null || !_tcpThread.IsAlive)
            {
                _tcpThread = StartThread(() => ListenTcpStreams());
            }
        }

        protected void RemoveClient(TcpClient client)
        {
            lock (_syncRoot)
            {
                var wrap = _tcpClients.Values.FirstOrDefault(p => p.TcpClient == client);
                if (wrap != null)
                {
                    _tcpClients.TryRemove(wrap.Id, out _);
                    wrap.Stream.Dispose();
                    //wrap.TcpClient.Close();
                    wrap.TcpClient.Dispose();

                    LogMsg($"Client {wrap.Sender.Name} disconnected");

                    if (_tcpClients.IsEmpty)
                    {
                        IsConnected = false;
                    }
                }
            }
        }

        private void ListenTcpStreams()
        {
            LogMsg("- starting tcp");

            _lastHeartbeat = DateTimeOffset.Now;
            try
            {
                while (true)
                {
                    if (Stopped) return;
                    bool wait = true;

                    if (!_tcpClients.IsEmpty)
                    {
                        Heartbeat();

                        while (_msgs.TryDequeue(out var request))
                        {
                            if (Stopped) return;

                            foreach (var client in _tcpClients.Values)
                            {
                                if (!SendToClient(client, request)) continue;

                                LogMsg($"sent {request.Data.Msg}");
                                wait = false;
                            }
                        }

                        foreach (var client in _tcpClients.Values)
                        {
                            if (!client.TcpClient.Connected) continue;
                            var stream = client.Stream;

                            while (stream.DataAvailable)
                            {
                                if (Stopped) return;

                                var fromSide = (Side)Read.Int(stream);
                                int id = Read.Int(stream);
                                var data = new Msg
                                {
                                    Text = Read.String(stream),
                                    Bytes = Read.Bytes(stream)
                                };


                                if (id == _heartbeatId) continue;

                                LogMsg($"read {data} from {client.Sender.Name}");

                                if (fromSide == Side)
                                {
                                    if (client.Busy.TryRemove(id, out var request))
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
                    }

                    if (wait)
                    {
                        if (Stopped) return;
                        _tcpWaitHandle.WaitOne(HiConst.SendDelay);
                    }
                }
            }
            catch (Exception exception) when (exception is SocketException ||
                                            exception.InnerException is SocketException)
            {
                LogMsg("Tcp disconnected");
            }
            catch (ThreadAbortException exception)
            {
                LogMsg("Tcp aborted");
            }
            catch (ThreadInterruptedException exception)
            {
                LogMsg("Tcp interrupted");
            }
            catch (Exception exception)
            {
                LogMsg(exception.ToString());
                LogMsg("Tcp disconnected due to exception");
            }
            finally
            {
                foreach (var clientWrap in _tcpClients.Values)
                {
                    clientWrap.Stream?.Close();
                    clientWrap.TcpClient?.Close();
                    if (clientWrap.Busy != null)
                    {
                        var canceled = clientWrap.Busy.ToList();
                        clientWrap.Busy.Clear();
                        foreach (var pair in canceled)
                        {
                            Complete(pair.Value, $"disconnected {clientWrap.Sender.Name}", default);
                        }
                    }
                }

                LogMsg("- stopped tcp");
                IsConnected = false;
                _tcpThread = null;
            }
        }

        private bool SendToClient(TcpClientWrap client, Request request)
        {
            if (!client.TcpClient.Connected) return false;
            if (request.Sender != null && request.Sender != client.Sender) return false;

            var stream = client.Stream;
            if (request.FromSide == Side) client.Busy.TryAdd(request.Id, request);

            Write.Int(stream, (int)request.FromSide);
            Write.Int(stream, request.Id);
            Write.String(stream, request.Data.Msg.Text);
            Write.Bytes(stream, request.Data.Msg.Bytes);

            return true;
        }

        private void ReceiveInvoke(Msg data, int id, Side fromSide, Sender sender)
        {
            if (ManualMessagePolling)
            {
                _repl.Enqueue(new DelayedReply(data, id, fromSide, sender));
            }
            else
            {
                var response = Receive?.Invoke(data, sender);
                EnqueueMsg(new Request(default, id, fromSide, sender));
            }
        }

        protected void LogMsg(string msg)
        {
            Log?.Invoke($"[{Side} : {_name}] {msg}");
        }

        private void EnqueueMsg(Request request)
        {
            _msgs.Enqueue(request);
            _tcpWaitHandle.Set();
        }

        private void Heartbeat()
        {
            if (DateTimeOffset.Now - _lastHeartbeat > TimeSpan.FromMilliseconds(HiConst.WatchPeriod))
            {
                _lastHeartbeat = DateTimeOffset.Now;
                foreach (var client in _tcpClients.Values)
                {
                    SendToClient(client, new Request(default, _heartbeatId, Side, null));
                }
            }
        }

        protected Thread StartThread(Action action)
        {
            //ThreadPool.QueueUserWorkItem(p => action());
            var threadStart = new ThreadStart(action);
            var thread = new Thread(threadStart);
            thread.Start();
            _threads.Add(thread);
            return thread;
        }

        public bool ManualMessagePolling { get; set; }

        public void PollMessages()
        {
            while (_done.TryDequeue(out var request))
            {
                if (Stopped) return;

                request.Continue();
            }

            while (_repl.TryDequeue(out var reply))
            {
                if (Stopped) return;
                var response = Receive?.Invoke(reply.Data, reply.Sender) ?? default;
                EnqueueMsg(new Request(response, reply.Id, reply.FromSide, reply.Sender));
            }
        }

        private void Complete(Request request, string error, Msg data)
        {
            request.Complete(error, data);

            if (ManualMessagePolling && !request.Blocking)
            {
                _done.Enqueue(request);
            }
            else
            {
                var thread = new Thread(() => request.Continue());
                thread.Start();
            }

            _completeHandle.Set();
            _completeHandle.Reset();
        }


        public async Task<ResponseData> Send(Msg msg, Sender sendTo = null)
        {
            if (!IsConnected) return new ResponseData();

            Request request = SendNew(msg, sendTo, false);

            var result = await request;
            return result.Data;
        }

        public ResponseData SendBlocking(Msg msg, int timeout = 0, Sender sendTo = null)
        {
            if (!IsConnected) return new ResponseData();

            var request = SendNew(msg, sendTo, true);

            if (timeout == 0) timeout = HiConst.SendBlockedTimeout;
            var stopwatch = Stopwatch.StartNew();
            while (!request.IsDone)
            {
                _completeHandle.WaitOne(timeout);
                if (stopwatch.ElapsedMilliseconds >= timeout) return new ResponseData { Error = "timeout" };
            }

            return request.Data;
        }

        private Request SendNew(Msg msg, Sender sendTo, bool blocking)
        {
            Request request;
            lock (_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                request = new Request(msg, _uk, Side, sendTo, blocking);
                EnqueueMsg(request);
            }

            return request;
        }

        protected void AlertStop()
        {
            Stopped = true;
            _tcpWaitHandle.Set();
            Thread.Sleep(100);
        }

        protected void Dispose()
        {
            while (_threads.Count > 0)
            {
                _threads.TryTake(out var thread);
                if (thread.IsAlive) thread.Join();
            }

            _tcpClients.Clear();
            _threads = new ConcurrentBag<Thread>();
            _msgs = new ConcurrentQueue<Request>();
            _done = new ConcurrentQueue<Request>();
            _uk = 0;
        }

        private class TcpClientWrap
        {
            public TcpClient TcpClient;
            public Sender Sender;
            public int Id;
            public NetworkStream Stream;

            public readonly ConcurrentDictionary<int, Request> Busy = new();
        }
        
        public string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }

            return string.Empty;
        }
    }

    public class Sender
    {
        public string Name { get; internal set; }
        public ConcurrentDictionary<string, object> DataBag { get; internal set; }

        internal Sender() { }
    }
}