using System;
using System.Threading.Tasks;

using NLog;
using CommandLine;

using PluxAdapter.Servers;
using PluxAdapter.Clients;

namespace PluxAdapter
{
    /// <summary>
    /// Main entry point from command line.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Program" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Main entry point into <see cref="PluxAdapter.Program" />, runs requested command.
        /// </summary>
        /// <param name="args">Command requested.</param>
        /// <returns><see cref="int" /> indicating command exit reason.</returns>
        public static async Task<int> Main(string[] args)
        {
            // parse args with default parser and map verbs
            int result = await Parser.Default.ParseArguments<Server.Options, Client.Options>(args).MapResult(
                // simply execute server
                (Server.Options options) => Execute(new Server(options)),
                // register callback on client before execution
                (Client.Options options) =>
                {
                    Client client = new Client(options);
                    client.FrameReceived += (sender, eventArgs) =>
                    {
                        // simply log received data
                        if (eventArgs.data.Count == 0) { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {eventArgs.device.path} with no data"); }
                        else { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {eventArgs.device.path} with data: {String.Join(" ", eventArgs.data)}"); }
                    };
                    return Execute(client);
                },
                // some gibberish, can't parse, fail
                errors => Task.FromResult(1));
            // execution done, flush loggers
            LogManager.Shutdown();
            return result;
        }

        /// <summary>
        /// Runs <paramref name="executable" /> loop, handles <see cref="System.Exception" /> and listens for <see cref="System.Console.CancelKeyPress" />.
        /// </summary>
        /// <param name="executable">Executable to run.</param>
        /// <returns><see cref="int" /> indicating <paramref name="executable" /> loop exit reason.</returns>
        private static async Task<int> Execute(IExecutable executable)
        {
            // register interrupt callback and execute executable
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                logger.Info("User interrupt requested");
                // cancel interrupt and shutdown gracefully
                eventArgs.Cancel = true;
                executable.Stop();
            };
            try { return await executable.Start(); }
            catch (Exception exc) { logger.Error(exc, "Something went wrong"); }
            finally { executable.Stop(); }
            return 1;
        }
    }
}
