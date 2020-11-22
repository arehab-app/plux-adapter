using System;
using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxAdapter
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
                (Client.Options options) =>
                {
                    Client client = new Client(options);
                    client.FrameReceived += (sender, eventArgs) =>
                    {
                        if (eventArgs.data.Count == 0) { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {eventArgs.device.path} with no data"); }
                        else { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {eventArgs.device.path} with data: {String.Join(" ", eventArgs.data)}"); }
                    };
                    return Execute(client);
                },
                errors => Task.FromResult(1));
            LogManager.Shutdown();
            return result;
        }

        private static async Task<int> Execute(IExecutable executable)
        {
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                logger.Info("User interrupt requested");
                eventArgs.Cancel = true;
                executable.Stop();
            };
            try { return await executable.Start(); }
            catch (Exception exc) { logger.Error(exc, "Something went wrong"); }
            return 1;
        }
    }
}
