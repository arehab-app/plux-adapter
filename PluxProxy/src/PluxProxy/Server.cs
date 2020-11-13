using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxProxy
{
    public sealed class Server : IExecutable
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

        private readonly Options options;
        private TcpListener server;
        private CancellationTokenSource source;
        private CancellationToken token;

        public Server(Options options) { this.options = options; }

        public async Task<int> Start()
        {
            IPAddress ipAddress = options.IPAddress is null ? IPAddress.Any : IPAddress.Parse(options.IPAddress);
            logger.Info($"Listening on {ipAddress}:{options.Port}");
            server = new TcpListener(ipAddress, options.Port);
            using (source = new CancellationTokenSource())
            {
                token = source.Token;
                server.Start();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Task<TcpClient> task = server.AcceptTcpClientAsync();
                        task.ContinueWith(Accept, token);
                        await task;
                    }
                }
                catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
                logger.Info("Cleaning up");
            }
            logger.Info("Shutting down");
            return 0;
        }

        public void Stop()
        {
            source?.Cancel();
            server?.Stop();
        }

        private async void Accept(Task<TcpClient> task)
        {
            using (TcpClient client = task.Result)
            using (NetworkStream stream = client.GetStream())
            {
                logger.Info($"Accepted connection from {client.Client.RemoteEndPoint} to {client.Client.LocalEndPoint}");
            }
        }
    }
}
