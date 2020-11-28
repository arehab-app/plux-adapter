using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;
using CommandLine;

namespace PluxAdapter
{
    /// <summary>
    /// Listens for connections from <see cref="PluxAdapter.Client" /> and manages <see cref="PluxAdapter.Handler" />.
    /// </summary>
    public sealed class Server : Program.IExecutable
    {
        /// <summary>
        /// <see cref="PluxAdapter.Server" /> configuration.
        /// </summary>
        [Verb("server", isDefault: true, HelpText = "Start server.")]
        public sealed class Options
        {
            /// <summary>
            /// IP to bind to.
            /// </summary>
            [Option("ip-address", HelpText = "(Default: all network interfaces) IP to bind to.")]
            public string IPAddress { get; private set; }

            /// <summary>
            /// Port to bind to.
            /// </summary>
            [Option("port", Default = 24242, HelpText = "Port to bind to.")]
            public int Port { get; private set; }

            /// <summary>
            /// Sensor update frequency.
            /// </summary>
            [Option("frequency", Default = 1000, HelpText = "Sensor update frequency.")]
            public float Frequency { get; private set; }

            /// <summary>
            /// Sensor data resolution.
            /// </summary>
            [Option("resolution", Default = 16, HelpText = "Sensor data resolution.")]
            public int Resolution { get; private set; }
        }

        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Server" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Managed <see cref="PluxAdapter.Handler" />.
        /// </summary>
        private readonly List<Handler> handlers = new List<Handler>();
        /// <summary>
        /// Parallel <see cref="System.Threading.Tasks.Task" /> used for <see cref="PluxAdapter.Handler" />.
        /// </summary>
        private readonly List<Task> tasks = new List<Task>();
        /// <summary>
        /// Configuration options.
        /// </summary>
        private readonly Options options;
        /// <summary>
        /// Underlying listener.
        /// </summary>
        private TcpListener server;
        /// <summary>
        /// <see cref="System.Threading.CancellationTokenSource" /> monitored by <see cref="PluxAdapter.Server" />.
        /// </summary>
        private CancellationTokenSource source;

        /// <summary>
        /// <see cref="PluxAdapter.Manager" /> used to manage <see cref="PluxAdapter.Device" />.
        /// </summary>
        public readonly Manager manager;

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Server" /> with <see cref="PluxAdapter.Server.Options" />.
        /// </summary>
        /// <param name="options">Configuration options.</param>
        public Server(Options options)
        {
            this.options = options;
            this.manager = new Manager(options.Frequency, options.Resolution);
        }

        /// <summary>
        /// Runs <see cref="PluxAdapter.Server" /> listening loop.
        /// </summary>
        /// <returns><see cref="int" /> indicating listening loop exit reason.</returns>
        public async Task<int> Start()
        {
            IPAddress ipAddress = options.IPAddress is null ? IPAddress.Any : IPAddress.Parse(options.IPAddress);
            logger.Info($"Listening on {ipAddress}:{options.Port}");
            server = new TcpListener(ipAddress, options.Port);
            using (source = new CancellationTokenSource())
            {
                try
                {
                    server.Start();
                    while (!source.IsCancellationRequested)
                    {
                        Handler handler = new Handler(this, await server.AcceptTcpClientAsync(), source.Token);
                        lock (handlers)
                        {
                            handlers.Add(handler);
                            tasks.Add(Task.Run(async () =>
                            {
                                try { await handler.Start(); }
                                catch (Exception exc) { logger.Error(exc, "Something went wrong"); }
                            }, source.Token));
                        }
                    }
                }
                catch (ObjectDisposedException) { if (!source.IsCancellationRequested) throw; }
                catch (NullReferenceException) { if (!source.IsCancellationRequested) throw; }
                finally { server.Stop(); }
            }
            logger.Info("Cleaning up");
            server = null;
            source = null;
            logger.Info("Shutting down");
            return 0;
        }

        /// <summary>
        /// Stops <see cref="PluxAdapter.Server" /> and it's monitored <see cref="PluxAdapter.Server.handlers" /> and <see cref="PluxAdapter.Server.tasks" />. This is threadsafe.
        /// </summary>
        public void Stop()
        {
            logger.Info("Stopping");
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            server?.Stop();
            lock (handlers)
            {
                foreach (Handler handler in handlers) { handler.Stop(); }
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();
                handlers.Clear();
            }
            manager.Stop();
        }
    }
}
