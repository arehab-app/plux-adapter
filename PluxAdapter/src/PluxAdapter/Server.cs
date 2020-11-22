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
    public sealed class Server : Program.IExecutable
    {
        [Verb("server", isDefault: true, HelpText = "Start server.")]
        public sealed class Options
        {
            [Option("ip-address", HelpText = "(Default: all network interfaces) IP to bind to.")]
            public string IPAddress { get; set; }

            [Option("port", Default = 24242, HelpText = "Port to bind to.")]
            public int Port { get; set; }

            [Option("frequency", Default = 1000, HelpText = "Sensor update frequency.")]
            public float Frequency { get; set; }

            [Option("resolution", Default = 16, HelpText = "Sensor data resolution.")]
            public int Resolution { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly List<Handler> handlers = new List<Handler>();
        private readonly List<Task> tasks = new List<Task>();
        private readonly Options options;
        private TcpListener server;
        private CancellationTokenSource source;

        public readonly Manager manager;

        public Server(Options options)
        {
            this.options = options;
            this.manager = new Manager(options.Frequency, options.Resolution);
        }

        public async Task<int> Start()
        {
            IPAddress ipAddress = options.IPAddress is null ? IPAddress.Any : IPAddress.Parse(options.IPAddress);
            logger.Info($"Listening on {ipAddress}:{options.Port}");
            server = new TcpListener(ipAddress, options.Port);
            using (source = new CancellationTokenSource())
            {
                server.Start();
                try
                {
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
            }
            logger.Info("Cleaning up");
            server = null;
            source = null;
            logger.Info("Shutting down");
            return 0;
        }

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
