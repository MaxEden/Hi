using System;
using System.Runtime.CompilerServices;

namespace Hi
{
    internal class Request : INotifyCompletion
    {
        public ResponseData Data;
        public int          Id;
        public bool         IsDone;
        public Side         FromSide;

        private Action _continuation;

        public bool IsCompleted => IsDone;

        public Request(string msg, int id, Side fromSide)
        {
            Data.Msg = msg;
            Id = id;
            FromSide = fromSide;
        }

        public void OnCompleted(Action continuation)
        {
            _continuation = continuation;
        }

        public Request GetAwaiter()
        {
            return this;
        }

        public Request GetResult()
        {
            return this;
        }

        public void Complete(string error, string response)
        {
            if (IsDone) throw new InvalidOperationException("Already completed");

            Data.Error = error;
            Data.ResponseMsg = response;
            IsDone = true;
        }

        public void Continue()
        {
            if (_continuation == null) throw new InvalidOperationException("Already continued");

            var tmp = _continuation;
            _continuation = null;
            tmp?.Invoke();
        }
    }

    internal enum Side
    {
        Server,
        Client
    }

    public struct ResponseData
    {
        public string Msg;
        public string ResponseMsg;
        public string Error;
    }
}