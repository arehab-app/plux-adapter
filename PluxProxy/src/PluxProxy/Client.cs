using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Client
    {
        [Verb("client", HelpText = "Start client.")]
        public sealed class Options
        {
            [Option("ip-address", Default = "127.0.0.1", HelpText = "IP to connect to.")]
            public string IPAddress { get; set; }

            [Option("port", Default = 24242, HelpText = "Port to connect to.")]
            public int Port { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<int> Start(Options options)
        {
            logger.Info("Starting client");
            return 0;
        }
    }
}
