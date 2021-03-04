using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Hi
{
    public class HiClient : HiBase
    {
        private string _serviceName;

        private TcpClient  _tcp;
        private IPEndPoint _serverEndPointUdp;
        private IPEndPoint _serverEndPointTcp;
        private string     _name;

        public HiClient(string name = null) : base(Side.Client)
        {
            _name = name;
        }

        public void WatchLoop()
        {
            while(true)
            {
                if(Stopped) return;
                Thread.Sleep(HiConst.WatchPeriod);
                if(Stopped) return;

                if(_tcp == null || (!_tcp.Connected && !IsConnected))
                {
                    TryConnect();
                }
            }
        }

        public bool Connect(string serviceName)
        {
            if(_name == null) _name = "a client without name";
            _serviceName = serviceName;
            var connected = TryConnect();
            StartThread(() => WatchLoop());
            return connected;
        }

        private bool TryConnect()
        {
            var name = _serviceName;

            var udp = new UdpClient {EnableBroadcast = true};
            var udpPort = GetPort(name);
            var ip = new IPEndPoint(IPAddress.Broadcast, udpPort);

            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(name);
                LogMsg("connect udp:" + udpPort);
                var sendTask = udp.SendAsync(bytes, bytes.Length, ip);
                //LogMsg("udp discovery sent");
                if(!sendTask.Wait(HiConst.UdpTimeout))
                {
                    //LogMsg("udp send timeout");
                    return false;
                }

                var receiveTask = udp.ReceiveAsync();
                if(!receiveTask.Wait(HiConst.UdpTimeout))
                {
                    //LogMsg("udp receive timeout");
                    return false;
                }

                var responseData = receiveTask.Result.Buffer;
                var responseString = Encoding.ASCII.GetString(responseData);


                var split = responseString.Split(':');
                if(split.Length < 2) return false;
                if(split[0] != name) return false;
                if(!Int32.TryParse(split[1], out int tcpPort)) return false;

                LogMsg("discovery succeeded");
                LogMsg("connect tcp:" + tcpPort);
                _serverEndPointUdp = receiveTask.Result.RemoteEndPoint;
                _serverEndPointTcp = new IPEndPoint(_serverEndPointUdp.Address, tcpPort);

                //==============
                _tcp = new TcpClient();
                _tcp.Client.NoDelay = true;
                _tcp.Client.SendTimeout = HiConst.SendTimeout;
                _tcp.Connect(_serverEndPointTcp);
                if(!_tcp.Connected) return false;

                NewClient(_tcp);
                //===============

                IsConnected = true;
                return true;
            }
            finally
            {
                udp.Close();
                udp.Dispose();
                udp = null;
            }
        }

        public void Disconnect()
        {
            AlertStop();

            _tcp.Close();
            _tcp.Dispose();
            _tcp = null;

            Dispose();
        }
    }
}