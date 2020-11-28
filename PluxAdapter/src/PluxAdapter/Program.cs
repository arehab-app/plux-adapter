﻿using System;
using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxAdapter
{
    /// <summary>
    /// Main entry point from command line.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Executable command.
        /// </summary>
        public interface IExecutable
        {
            /// <summary>
            /// Runs <see cref="PluxAdapter.Program.IExecutable" /> loop.
            /// </summary>
            /// <returns><see cref="int" /> indicating <see cref="PluxAdapter.Program.IExecutable" /> loop exit reason.</returns>
            Task<int> Start();
            /// <summary>
            /// Stops <see cref="PluxAdapter.Program.IExecutable" />. This is threadsafe.
            /// </summary>
            void Stop();
        }

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

        /// <summary>
        /// Runs <paramref name="executable" /> loop, handles <see cref="System.Exception" /> and listens for <see cref="System.Console.CancelKeyPress" />.
        /// </summary>
        /// <param name="executable">Executable to run.</param>
        /// <returns><see cref="int" /> indicating <paramref name="executable" /> loop exit reason.</returns>
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
            finally { executable.Stop(); }
            return 1;
        }
    }
}
