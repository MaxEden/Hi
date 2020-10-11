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
        private UdpClient            _udp;
        private TcpClient            _tcp;
        private IPEndPoint           _serverEndPointUdp;
        private IPEndPoint           _serverEndPointTcp;

        public HiClient() : base(Side.Client) { }

        public async Task<bool> Connect(string name)
        {
            _name = name;
            _udp = new UdpClient();
            _udp.EnableBroadcast = true;
            var ip = new IPEndPoint(IPAddress.Broadcast, HiConst.UdpPort);
            
            byte[] bytes = Encoding.ASCII.GetBytes(name);
            int size = await _udp.SendAsync(bytes, bytes.Length, ip);
            
            var receiveTask = _udp.ReceiveAsync();
            if (!receiveTask.Wait(100000)) return false;
            
            var responseData = receiveTask.Result.Buffer;
            var responseString = Encoding.ASCII.GetString(responseData);
            
            if (responseString != name + "!!!") return false;
            
            _serverEndPointUdp = receiveTask.Result.RemoteEndPoint;

            // _serverEndPointUdp = new IPEndPoint(IPAddress.Parse("127.0.0.1"), HiConst.UdpPort);
            _serverEndPointTcp = new IPEndPoint(_serverEndPointUdp.Address, _serverEndPointUdp.Port + 1);

            //==============
            _tcp = new TcpClient();
            _tcp.Client.NoDelay = true;
            _tcp.Client.SendTimeout = 1000;
            _tcp.Connect(_serverEndPointTcp);
            StartThread(() => ListenTcpStreams(_tcp));

            //===============

            IsConnected = true;
            return true;
        }
    }
}