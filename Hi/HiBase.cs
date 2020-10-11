using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private readonly ConcurrentQueue<Request>           _msgs = new ConcurrentQueue<Request>();
        private readonly ConcurrentDictionary<int, Request> _busy = new ConcurrentDictionary<int, Request>();
        private readonly ConcurrentQueue<Request>           _done = new ConcurrentQueue<Request>();

        internal HiBase(Side side)
        {
            Side = side;
        }

        private protected void ListenTcpStreams(TcpClient client)
        {
            var stream = client.GetStream();
            try
            {
                while (client.Connected)
                {
                    bool wait = true;
                    while (_msgs.TryDequeue(out var request))
                    {
                        if (request.FromSide == Side) _busy.TryAdd(request.Id, request);

                        WriteInt(stream, (int)request.FromSide);
                        WriteInt(stream, request.Id);
                        WriteString(stream, request.Data.Msg);

                        Console.WriteLine($"[{Side}] sent {request.Data.Msg}");

                        wait = false;
                    }

                    while (stream.DataAvailable)
                    {
                        var fromSide = (Side)ReadInt(stream);
                        int id = ReadInt(stream);
                        var data = ReadString(stream);

                        Console.WriteLine($"[{Side}] read {data}");

                        if (fromSide == Side)
                        {
                            if (_busy.TryRemove(id, out var request))
                            {
                                Complete(request, data);
                            }
                        }
                        else
                        {
                            var response = Receive?.Invoke(data);
                            _msgs.Enqueue(new Request(response, id, fromSide));
                        }

                        wait = false;
                    }

                    if (wait)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            finally
            {
                stream.Close();
                client.Close();
            }
        }

        protected void StartThread(Action action)
        {
            var threadStart = new ThreadStart(action);
            var thread = new Thread(threadStart);
            thread.Start();
        }

        public bool ManualMessagePull { get; set; }

        public void PullMessages()
        {
            while (_done.TryDequeue(out var request))
            {
                request.Continue();
            }
        }

        private void Complete(Request request, string data)
        {
            request.Complete(null, data);

            if (ManualMessagePull)
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
            if (bufferSize == -1) return null;
            if (bufferSize == 0) return "";
            
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
            if (str == null)
            {
                WriteInt(stream, -1);
                return;
            }
        
            if (str == "")
            {
                WriteInt(stream, 0);
                return;
            }
            
            var buffer = Encoding.UTF8.GetBytes(str);
            WriteInt(stream, buffer.Length);
            stream.Write(buffer, 0, buffer.Length);
        }

        public Func<string, string> Receive;

        public async Task<ResponseData> Send(string msg)
        {
            Request request;
            lock (_syncRoot)
            {
                unchecked
                {
                    _uk++;
                }

                request = new Request(msg, _uk, Side);
                _msgs.Enqueue(request);
            }

            var result = await request;
            return result.Data;
        }

        public bool IsConnected { get; set; }
    }
}