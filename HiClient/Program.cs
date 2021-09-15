using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hi;

namespace HiClientApp
{
    class Program
    {
        private static HiClient _client;

        static void Main(string[] args)
        {
            var title = "PID:" + Process.GetCurrentProcess().Id;
            Console.WriteLine(title);
            Console.Title = title;
            
            Start();
            
            while(!(Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape))
            {
                Thread.Sleep(100);

                _client.PollMessages();
            }
        }

        static async void Start()
        {
            Console.WriteLine("Hello World!");
            
            _client = new HiClient();
            _client.Receive = Receive;
            _client.ManualMessagePolling = true;

            _client.Log = s => {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(s);
                Console.ResetColor();
            };
            
            var isConnected = _client.Connect("DrawBoard");

            if (!isConnected)
            {
                Console.WriteLine("Couldn't connect!");
                return;
            }
            await _client.Send("hi server!");

            _client.SendBlocking(new Msg{
                
                Text = "I SAID \"HI SERVER\"!",
                Bytes = new byte[3] {1,2,3}
            });
        }

        private static Msg Receive(Msg arg, Sender sender)
        {
            return "No you!" + arg;
        }
    }
}
