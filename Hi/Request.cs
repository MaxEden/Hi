using System;
using System.Runtime.CompilerServices;

namespace Hi
{
    internal class Request : INotifyCompletion
    {
        public          ResponseData Data;
        public readonly int          Id;
        public volatile bool         IsDone;
        public readonly Side         FromSide;
        public readonly Sender       Sender;
        public readonly bool         Blocking;

        private Action _continuation;
        private bool   _hasContinuation;


        public bool IsCompleted => IsDone;

        public Request(Msg msg, int id, Side fromSide, Sender sender, bool blocking = false)
        {
            Data.Msg = msg;
            Id = id;
            FromSide = fromSide;
            Sender = sender;
            Blocking = blocking;
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

        public void Complete(string error, Msg response)
        {
            if(IsDone) throw new InvalidOperationException("Already completed");

            Data.Error = error;
            Data.ResponseMsg = response;

            IsDone = true;
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
        public readonly Msg Data;
        public readonly int    Id;
        public readonly Side   FromSide;
        public readonly Sender Sender;

        public DelayedReply(Msg data, int id, Side fromSide, Sender sender)
        {
            Data = data;
            Id = id;
            FromSide = fromSide;
            Sender = sender;
        }
    }

    internal enum Side
    {
        Server,
        Client
    }

    public struct ResponseData
    {
        public Msg Msg;
        public Msg ResponseMsg;
        public string Error;
    }
}