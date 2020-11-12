using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Program
    {
        [Verb("server", isDefault: true, HelpText = "Start server")]
        private sealed class ServerOptions
        {
            [Option('t', "todo", Default = "TODO", HelpText = "TODO")]
            public string TODO { get; set; }
        }

        [Verb("client", HelpText = "Start client")]
        private sealed class ClientOptions
        {
            [Option('t', "todo", Default = "TODO", HelpText = "TODO")]
            public string TODO { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<int> Main(string[] args)
        {
            int result = await Parser.Default.ParseArguments<ServerOptions, ClientOptions>(args).MapResult(
                (ServerOptions options) => Start(options),
                (ClientOptions options) => Start(options),
                errors => Task.FromResult(1));
            LogManager.Shutdown();
            return result;
        }

        private static async Task<int> Start(ServerOptions options)
        {
            logger.Info("Starting server");
            logger.Debug(options.TODO);
            return 0;
        }

        private static async Task<int> Start(ClientOptions options)
        {
            logger.Info("Starting client");
            logger.Debug(options.TODO);
            return 0;
        }
    }
}
