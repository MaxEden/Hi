using Hi;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HiServerApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var title = "PID:" + Process.GetCurrentProcess().Id;
            Console.WriteLine(title);
            Console.Title = title;
            
            var app = new App();
            app.Start();
            while(!(Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape))
            {
                await app.Update();
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

            public Msg Receive(Msg msg, Sender sender)
            {
                return "Ok, " + msg + ".";
            }

            static  DateTimeOffset _lastTime;
            private HiServer       _server;

            public async Task Update()
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