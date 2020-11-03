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
        private bool   _hasContinuation;

        public bool IsCompleted => IsDone;

        public Request(string msg, int id, Side fromSide)
        {
            Data.Msg = msg;
            Id = id;
            FromSide = fromSide;
        }

        public void OnCompleted(Action continuation)
        {
            _hasContinuation = true;
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
            IsDone = true;
            Data.Error = error;
            Data.ResponseMsg = response;
        }

        public void Continue()
        {
            if(!_hasContinuation) return;
            
            var tmp = _continuation;
            _continuation = null;
            if(tmp == null) throw new InvalidOperationException("Already continued");
            tmp?.Invoke();
        }
    }

    internal class DelayedReply
    {
        public readonly string Data;
        public readonly int    Id;
        public readonly Side   FromSide;

        public DelayedReply(string data, int id, Side fromSide)
        {
            Data = data;
            Id = id;
            FromSide = fromSide;
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