using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hi
{
    public class HiServer : HiBase
    {
        private UdpClient   _udp;
        private TcpListener _tcp;
        private string      _name;
        private IPEndPoint  _udpEndpoint;

        public HiServer() : base(Side.Server) {}

        public void Open(string name)
        {
            _name = name;

            StartThread(() => ListenUdp(HiConst.UdpPort));
            StartThread(() => ListenTcp(HiConst.UdpPort + 1));
        }

        void ListenUdp(int port)
        {
            _udpEndpoint = new IPEndPoint(IPAddress.Any, port);
            var clientEndPoint = new IPEndPoint(IPAddress.Any, port);
            _udp = new UdpClient(_udpEndpoint);

            while(true)
            {
                if(Stopped) return;

                try
                {
                    byte[] bytes = _udp.Receive(ref clientEndPoint);
                    string password = Encoding.ASCII.GetString(bytes);

                    if(password == _name)
                    {
                        var responseData = Encoding.ASCII.GetBytes(password);
                        _udp.Send(responseData, responseData.Length, clientEndPoint);
                    }
                }
                catch(SocketException e) when(e.SocketErrorCode == SocketError.Interrupted)
                {
                    Log?.Invoke("Udp closed");
                    return;
                }
                catch(Exception exception)
                {
                    Log?.Invoke(exception.ToString());
                    _udp.Dispose();
                    _udp = new UdpClient(_udpEndpoint);
                }
            }
        }

        void ListenTcp(int port)
        {
            var clientEndPoint = new IPEndPoint(IPAddress.Any, port);
            _tcp = new TcpListener(clientEndPoint);
            _tcp.Server.NoDelay = true;
            _tcp.Server.SendTimeout = HiConst.SendTimeout;
            _tcp.Start();

            while(true)
            {
                if(Stopped) return;

                try
                {
                    TcpClient client = _tcp.AcceptTcpClient();
                    Log?.Invoke("Client connected");
                    IsConnected = true;
                    ListenTcpStreams(client);
                }
                catch(SocketException e) when(e.SocketErrorCode == SocketError.Interrupted)
                {
                    Log?.Invoke("Tcp closed");
                    IsConnected = false;
                }
                catch(Exception exception)
                {
                    Log?.Invoke(exception.ToString());
                    Close();
                }
            }
        }

        public void Close()
        {
            AlertStop();

            _udp.Close();
            _udp.Dispose();
            _udp = null;

            _tcp.Stop();
            _tcp = null;

            Dispose();
        }
    }
}