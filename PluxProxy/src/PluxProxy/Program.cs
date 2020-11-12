using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            int result = await Parser.Default.ParseArguments<Server.Options, Client.Options>(args).MapResult(
                (Server.Options options) => Server.Start(options),
                (Client.Options options) => Client.Start(options),
                errors => Task.FromResult(1));
            LogManager.Shutdown();
            return result;
        }
    }
}
