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
