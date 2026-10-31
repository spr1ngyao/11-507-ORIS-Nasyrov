using System;
using System.Threading.Tasks;

namespace IndependentWork;

class Program
{
    static async Task Main(string[] args)
    {
        HttpServer server = new HttpServer();

        server.Start();
        Task listening = server.ListenAsync();

        Console.WriteLine("Для остановки введи stop:");

        while (Console.ReadLine() != "stop")
        {
            Console.WriteLine("Для остановки введи stop:");
        }

        server.Stop();
        await listening;
    }
}