using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hi
{
    public class HiClient : HiBase
    {
        private string _name;

        public  Func<string, string> Receive;
        private TcpClient            _tcp;
        private IPEndPoint           _serverEndPointUdp;
        private IPEndPoint           _serverEndPointTcp;

        public HiClient() : base(Side.Client) { }

        public async void WatchLoop()
        {
            while (true)
            {
                Thread.Sleep(HiConst.WatchPeriod);
                if (_tcp == null || (!_tcp.Connected && !IsConnected))
                {
                    await TryConnect();
                }
            }
        }

        public async Task<bool> Connect(string name)
        {
            _name = name;
            var connected = await TryConnect();
            StartThread(() => WatchLoop());
            return connected;
        }

        public async Task<bool> TryConnect()
        {
            var name = _name;

            using var udp = new UdpClient {EnableBroadcast = true};
            var ip = new IPEndPoint(IPAddress.Broadcast, HiConst.UdpPort);

            byte[] bytes = Encoding.ASCII.GetBytes(name);
            int size = await udp.SendAsync(bytes, bytes.Length, ip);

            var receiveTask = udp.ReceiveAsync();
            if (!receiveTask.Wait(HiConst.UdpTimeout)) return false;

            var responseData = receiveTask.Result.Buffer;
            var responseString = Encoding.ASCII.GetString(responseData);

            if (responseString != name) return false;

            _serverEndPointUdp = receiveTask.Result.RemoteEndPoint;
            _serverEndPointTcp = new IPEndPoint(_serverEndPointUdp.Address, _serverEndPointUdp.Port + 1);

            //==============
            _tcp = new TcpClient();
            _tcp.Client.NoDelay = true;
            _tcp.Client.SendTimeout = 1000;
            _tcp.Connect(_serverEndPointTcp);
            if (!_tcp.Connected) return false;

            StartThread(() => ListenTcpStreams(_tcp));
            //===============

            IsConnected = true;
            return true;
        }
    }
}