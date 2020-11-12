using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Program
    {
        private sealed class Options
        {
            [Option('t', "todo", Default="TODO", HelpText="TODO")]
            public string TODO { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<int> Main(string[] args)
        {
            int result = await Parser.Default.ParseArguments<Options>(args).MapResult(Execute, _ => Task.FromResult(1));
            LogManager.Shutdown();
            return result;
        }

        private static async Task<int> Execute(Options options)
        {
            logger.Debug(options.TODO);
            return 0;
        }
    }
}
