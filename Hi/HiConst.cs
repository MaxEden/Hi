namespace Hi
{
    public class HiConst
    {
        public static int UdpTimeout => 1000;
        public static int WatchPeriod = 10;
        public static int UdpPort     = 5151;
        public static int TcpPort => UdpPort + 1;
    }
}