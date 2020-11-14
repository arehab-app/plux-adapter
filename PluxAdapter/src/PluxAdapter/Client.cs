using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using NLog;
using CommandLine;

namespace PluxAdapter
{
    public sealed class Client : Program.IExecutable
    {
        [Verb("client", HelpText = "Start client.")]
        public sealed class Options
        {
            [Option("ip-address", Default = "127.0.0.1", HelpText = "IP to connect to.")]
            public string IPAddress { get; set; }

            [Option("port", Default = 24242, HelpText = "Port to connect to.")]
            public int Port { get; set; }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly Options options;
        private TcpClient client;
        private CancellationTokenSource source;
        private CancellationToken token;

        public Client(Options options) { this.options = options; }

        public async Task<int> Start()
        {
            IPAddress ipAddress = IPAddress.Parse(options.IPAddress);
            logger.Info($"Connecting to {ipAddress}:{options.Port}");
            using (client = new TcpClient())
            using (source = new CancellationTokenSource())
            {
                token = source.Token;
                try
                {
                    await client.ConnectAsync(ipAddress, options.Port);
                    using (NetworkStream stream = client.GetStream())
                    {
                        logger.Info($"Connected to {client.Client.RemoteEndPoint} from {client.Client.LocalEndPoint}");
                    }
                }
                catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
                catch (NullReferenceException) { if (!token.IsCancellationRequested) throw; }
                logger.Info("Cleaning up");
            }
            logger.Info("Shutting down");
            return 0;
        }

        public void Stop()
        {
            source?.Cancel();
            client?.Close();
        }
    }
}
