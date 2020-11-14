using System;
using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Program
    {
        public interface IExecutable
        {
            Task<int> Start();
            void Stop();
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<int> Main(string[] args)
        {
            int result = await Parser.Default.ParseArguments<Server.Options, Client.Options>(args).MapResult(
                (Server.Options options) => Execute(new Server(options)),
                (Client.Options options) => Execute(new Client(options)),
                errors => Task.FromResult(1));
            LogManager.Shutdown();
            return result;
        }

        public static async Task<int> Execute(IExecutable executable)
        {
            Console.CancelKeyPress += (sender, e) => {
                logger.Info("User interrupt requested");
                e.Cancel = true;
                executable.Stop();
            };
            return await executable.Start();
        }
    }
}
