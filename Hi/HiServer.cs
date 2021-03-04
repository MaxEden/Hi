using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Hi
{
    public class HiServer : HiBase
    {
        private UdpClient   _udp;
        private TcpListener _tcp;
        private string      _name;
        private IPEndPoint  _udpEndpoint;
        private int         _tcpPort;

        public HiServer() : base(Side.Server) {}

        public void Open(string name)
        {
            _name = name;
            StartThread(() => ListenUdp(GetPort(name)));
            StartThread(() => ListenTcp(0));
        }

        void ListenUdp(int port)
        {
            LogMsg("listen udp:" + port);
            _udpEndpoint = new IPEndPoint(IPAddress.Any, port);
            var clientEndPoint = new IPEndPoint(IPAddress.Any, port);
            _udp = new UdpClient(_udpEndpoint);

            while(true)
            {
                if(Stopped) return;

                try
                {
                    byte[] bytes = _udp.Receive(ref clientEndPoint);
                    if(Stopped) return;
                    if(_tcpPort == 0) return;

                    string password = Encoding.ASCII.GetString(bytes);
                    LogMsg("client discovery received: " + password);

                    if(password == _name)
                    {
                        var responseData = Encoding.ASCII.GetBytes(password + ":" + _tcpPort);
                        LogMsg("client discovery accepted");
                        _udp.Send(responseData, responseData.Length, clientEndPoint);
                    }
                    else
                    {
                        LogMsg("client discovery denied");
                    }
                }
                catch(Exception exception) when(exception is SocketException ||
                                                exception.InnerException is SocketException)
                {
                    LogMsg("Udp closed");
                    return;
                }
                catch(Exception exception)
                {
                    LogMsg(exception.ToString());
                    LogMsg("Udp closed due to exception");
                    _udp.Dispose();
                    _udp = new UdpClient(_udpEndpoint);
                }
            }
        }

        void ListenTcp(int port)
        {
            LogMsg("listen tcp");
            var clientEndPoint = new IPEndPoint(IPAddress.Any, port);
            _tcp = new TcpListener(clientEndPoint);
            _tcp.Server.NoDelay = true;
            _tcp.Server.SendTimeout = HiConst.SendTimeout;
            _tcp.Start();

            _tcpPort = ((IPEndPoint)_tcp.LocalEndpoint).Port;
            LogMsg("started tcp:" + _tcpPort);

            while(true)
            {
                if(Stopped) return;

                try
                {
                    TcpClient client = _tcp.AcceptTcpClient();
                    LogMsg("Client connected");
                    IsConnected = true;
                    NewClient(client);
                }
                catch(Exception exception) when(exception is SocketException ||
                                                exception.InnerException is SocketException)
                {
                    LogMsg("Tcp closed by client");
                    IsConnected = false;
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