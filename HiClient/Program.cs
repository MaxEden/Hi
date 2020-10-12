using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hi;

namespace HiClientApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var title = "PID:" + Process.GetCurrentProcess().Id;
            Console.WriteLine(title);
            Console.Title = title;
            
            Start();
            
            while(!(Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape))
            {
                Thread.Sleep(100);
            }
        }

        static async void Start()
        {
            Console.WriteLine("Hello World!");
            
            var client = new HiClient();
            client.Receive = Receive;
            client.Log = s => {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(s);
                Console.ResetColor();
            };
            
            var isConnected = await client.Connect("DrawBoard");

            if (!isConnected)
            {
                Console.WriteLine("Couldn't connect!");
                return;
            }
            await client.Send("hi server!");
        }

        private static string Receive(string arg)
        {
            return "No you!" + arg;
        }
    }
}
