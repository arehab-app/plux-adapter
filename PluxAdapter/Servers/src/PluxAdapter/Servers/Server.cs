using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;
using CommandLine;

namespace PluxAdapter.Servers
{
    /// <summary>
    /// Listens for connections from <see cref="PluxAdapter.Clients.Client" /> and manages <see cref="PluxAdapter.Servers.Handler" />.
    /// </summary>
    public sealed class Server : IExecutable
    {
        /// <summary>
        /// <see cref="PluxAdapter.Servers.Server" /> configuration.
        /// </summary>
        [Verb("server", isDefault: true, HelpText = "Start server.")]
        public sealed class Options
        {
            /// <summary>
            /// IP to bind to.
            /// </summary>
            [Option("ip-address", HelpText = "(Default: all network interfaces) IP to bind to.")]
            public string IPAddress { get; }

            /// <summary>
            /// Port to bind to.
            /// </summary>
            [Option("port", Default = 24242, HelpText = "Port to bind to.")]
            public int Port { get; }

            /// <summary>
            /// Sensor update frequency.
            /// </summary>
            [Option("frequency", Default = 1000, HelpText = "Sensor update frequency.")]
            public float Frequency { get; }

            /// <summary>
            /// Sensor data resolution.
            /// </summary>
            [Option("resolution", Default = 16, HelpText = "Sensor data resolution.")]
            public int Resolution { get; }

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Servers.Server.Options" />.
            /// </summary>
            /// <param name="ipAddress">IP to bind to.</param>
            /// <param name="port">Port to bind to.</param>
            /// <param name="frequency">Sensor update frequency.</param>
            /// <param name="resolution">Sensor data resolution.</param>
            public Options(string ipAddress, int port, float frequency, int resolution)
            {
                IPAddress = ipAddress;
                Port = port;
                Frequency = frequency;
                Resolution = resolution;
            }
        }

        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Servers.Server" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Managed <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        private readonly List<Handler> handlers = new List<Handler>();
        /// <summary>
        /// Parallel <see cref="System.Threading.Tasks.Task" /> used for <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        private readonly List<Task> tasks = new List<Task>();
        /// <summary>
        /// Underlying listener.
        /// </summary>
        private TcpListener server;
        /// <summary>
        /// <see cref="System.Threading.CancellationTokenSource" /> monitored by <see cref="PluxAdapter.Servers.Server" />.
        /// </summary>
        private CancellationTokenSource source;

        /// <summary>
        /// Configuration options.
        /// </summary>
        public readonly Options options;
        /// <summary>
        /// <see cref="PluxAdapter.Servers.Manager" /> used to manage <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        public readonly Manager manager;

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Servers.Server" /> with <see cref="PluxAdapter.Servers.Server.Options" />.
        /// </summary>
        /// <param name="options">Configuration options.</param>
        public Server(Options options)
        {
            this.options = options;
            this.manager = new Manager(options.Frequency, options.Resolution);
        }

        /// <summary>
        /// Runs <see cref="PluxAdapter.Servers.Server" /> listening loop.
        /// </summary>
        /// <returns><see cref="int" /> indicating listening loop exit reason.</returns>
        public async Task<int> Start()
        {
            // parse ip and create server and source
            IPAddress ipAddress = options.IPAddress is null ? IPAddress.Any : IPAddress.Parse(options.IPAddress);
            logger.Info($"Listening on {ipAddress}:{options.Port}");
            server = new TcpListener(ipAddress, options.Port);
            using (source = new CancellationTokenSource())
            {
                try
                {
                    // start server and wait for client connections
                    server.Start();
                    while (!source.IsCancellationRequested)
                    {
                        Handler handler = new Handler(this, await server.AcceptTcpClientAsync(), source.Token);
                        lock (handlers)
                        {
                            // client connected, register handler and execute it in parallel
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
        /// Stops <see cref="PluxAdapter.Servers.Server" /> and it's monitored <see cref="PluxAdapter.Servers.Server.handlers" /> and <see cref="PluxAdapter.Servers.Server.tasks" />. This is threadsafe.
        /// </summary>
        public void Stop()
        {
            logger.Info("Stopping");
            // always cancel token first
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            // stop server, handlers and manager
            server?.Stop();
            lock (handlers)
            {
                foreach (Handler handler in handlers) { handler.Stop(); }
                // wait for handlers to shutdown gracefully
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();
                handlers.Clear();
            }
            manager.Stop();
        }
    }
}
