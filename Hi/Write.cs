using System;
using System.Net.Sockets;
using System.Text;

namespace Hi
{
    internal static class Write
    {
        public static void Int(NetworkStream stream, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            stream.Write(buffer, 0, buffer.Length);
        }

        public static void String(NetworkStream stream, string str)
        {
            if (str == null)
            {
                Int(stream, -1);
                return;
            }

            if (str == "")
            {
                Int(stream, 0);
                return;
            }

            var buffer = Encoding.UTF8.GetBytes(str);
            Int(stream, buffer.Length);
            stream.Write(buffer, 0, buffer.Length);
        }

        public static void Bytes(NetworkStream stream, byte[] bytes)
        {
            if (bytes == null)
            {
                Int(stream, -1);
                return;
            }

            if (bytes.Length == 0)
            {
                Int(stream, 0);
                return;
            }

            Int(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

}