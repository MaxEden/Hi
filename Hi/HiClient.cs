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

        private TcpClient _tcp;
        private IPEndPoint _serverEndPointUdp;
        private IPEndPoint _serverEndPointTcp;
        

        public HiClient(string name = null) : base(Side.Client)
        {
            _name = name;
        }

        private void WatchLoop()
        {
            while (true)
            {
                if (Stopped) return;
                Thread.Sleep(HiConst.WatchPeriod);
                if (Stopped) return;

                if (_tcp == null || (!_tcp.Connected && !IsConnected))
                {
                    TryConnect();
                }
            }
        }

        public bool Connect(string serviceName)
        {
            if (_name == null) _name = "a client without name";
            _serviceName = serviceName;

            LogMsg($"client at {GetLocalIPAddress()}");

            var connected = TryConnect();
            StartThread(() => WatchLoop());
            return connected;
        }

        private bool TryConnect()
        {
            var serviceName = _serviceName;
            int udpPort = GetPort(serviceName);
            int localPort = udpPort + 1;
            var ip = new IPEndPoint(IPAddress.Broadcast, udpPort);
            var localIp = new IPEndPoint(IPAddress.Any, localPort);

            var udp = new UdpClient(localIp) { EnableBroadcast = true };
            
            LogMsg("local udp receiver: " + localPort);
            LogMsg("discovering udp:" + udpPort);
            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(serviceName);
                //LogMsg("connect udp:" + udpPort);
                var sendTask = udp.SendAsync(bytes, bytes.Length, ip);
                //LogMsg("udp discovery sent");
                if (!sendTask.Wait(HiConst.UdpTimeout))
                {
                    //LogMsg("udp send timeout");
                    return false;
                }

                var receiveTask = udp.ReceiveAsync();
                if (!receiveTask.Wait(HiConst.UdpTimeout))
                {
                    //LogMsg("udp receive timeout");
                    return false;
                }

                var responseData = receiveTask.Result.Buffer;
                var responseString = Encoding.ASCII.GetString(responseData);

                var split = responseString.Split(':');
                if (split.Length < 2) return false;
                if (split[0] != serviceName) return false;
                if (!Int32.TryParse(split[1], out int tcpPort)) return false;


                _serverEndPointUdp = receiveTask.Result.RemoteEndPoint;

                LogMsg($"discovery succeeded {_serverEndPointUdp.Address}:{tcpPort}");
                LogMsg($"connect tcp: {_serverEndPointUdp.Address}:{tcpPort}");

                _serverEndPointTcp = new IPEndPoint(_serverEndPointUdp.Address, tcpPort);

                //==============
                _tcp = new TcpClient();
                _tcp.Client.NoDelay = true;
                _tcp.Client.SendTimeout = HiConst.SendTimeout;
                _tcp.Connect(_serverEndPointTcp);
                if (!_tcp.Connected) return false;

                NewClient(_tcp, _name, _serviceName);
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

            if (_tcp != null)
            {
                _tcp.Close();
                _tcp.Dispose();
                _tcp = null;
            }

            Dispose();
        }
    }
}