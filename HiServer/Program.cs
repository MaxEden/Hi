using Hi;
using System;

namespace HiServerApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var app = new App();
            app.Start();
            while(!(Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape))
            {
                app.Update();
            }
        }

        class App
        {
            public void Start()
            {
                Console.WriteLine("Hello World!");
                
                _server = new Hi.HiServer();
                _server.Receive = Receive;
                _server.Log = s =>
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine(s);
                    Console.ResetColor();
                };
                
                _server.Open("DrawBoard");
            }

            public string Receive(string msg)
            {
                return "Ok, " + msg + ".";
            }

            static  DateTimeOffset _lastTime;
            private HiServer       _server;

            public async void Update()
            {
                if(DateTimeOffset.Now - _lastTime > TimeSpan.FromSeconds(2))
                {
                    _lastTime = DateTimeOffset.Now;
                    if(!_server.IsConnected) return;
                    var response = await _server.Send("Lolka!");
                    Console.WriteLine(response.ResponseMsg);
                }
            }
        }
    }
}