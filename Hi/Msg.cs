namespace Hi
{
    public struct Msg
    {
        public string Text;
        public byte[] Bytes;

        public static implicit operator Msg(string str)
        {
            return new Msg
            {
                Text = str,
                Bytes = null
            };
        }

        public static implicit operator Msg(byte[] bytes)
        {
            return new Msg
            {
                Text = null,
                Bytes = bytes
            };
        }

        public override string ToString()
        {
            var str = Text;
            if (Bytes != null) str += $" +{Bytes.Length}bytes";
            return str;
        }
    }
}