using System;
using System.Net.Sockets;
using System.Text;

namespace Hi
{
    internal static class Read
    {

        public static int Int(NetworkStream stream)
        {
            byte[] buffer = new byte[4];
            stream.Read(buffer, 0, buffer.Length);
            int value = BitConverter.ToInt32(buffer, 0);
            return value;
        }

        public static string String(NetworkStream stream)
        {
            int bufferSize = Int(stream);
            if (bufferSize == -1) return null;
            if (bufferSize == 0) return "";

            byte[] buffer = new byte[bufferSize];
            stream.Read(buffer, 0, buffer.Length);
            var value = Encoding.UTF8.GetString(buffer);
            return value;
        }

        public static byte[] Bytes(NetworkStream stream)
        {
            int bufferSize = Int(stream);
            if (bufferSize == -1) return null;
            if (bufferSize == 0) return Array.Empty<byte>();

            byte[] buffer = new byte[bufferSize];
            stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }
    }

}