using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Server
    {
        [Verb("server", isDefault: true, HelpText = "Start server.")]
        public sealed class Options
        {
            [Option("ip-address", HelpText = "(Default: all network interfaces) IP to bind to.")]
            public string IPAddress { get; set; }

            [Option("port", Default = 24242, HelpText = "Port to bind to.")]
            public int Port { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<int> Start(Options options)
        {
            logger.Info("Starting server");
            return 0;
        }
    }
}
