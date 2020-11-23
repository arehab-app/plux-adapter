using System;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
            public string IPAddress { get; private set; }

            [Option("port", Default = 24242, HelpText = "Port to connect to.")]
            public int Port { get; private set; }

            [Option("paths", HelpText = "(Default: all reachable paths) Paths of devices to request.")]
            public IEnumerable<string> Paths { get; private set; }
        }

        public sealed class Device
        {
            public readonly string path;
            public readonly string description;
            public readonly float frequency;
            public readonly ReadOnlyCollection<Source> sources;

            public Device(string path, string description, float frequency, List<Source> sources)
            {
                this.path = path;
                this.description = description;
                this.frequency = frequency;
                this.sources = sources.AsReadOnly();
            }
        }

        public sealed class Source
        {
            public readonly int port;
            public readonly int frequencyDivisor;
            public readonly int resolution;
            public readonly int channelMask;

            public Source(int port, int frequencyDivisor, int resolution, int channelMask)
            {
                this.port = port;
                this.frequencyDivisor = frequencyDivisor;
                this.resolution = resolution;
                this.channelMask = channelMask;
            }
        }

        public sealed class FrameReceivedEventArgs : EventArgs
        {
            public readonly int lastFrame;
            public readonly int currentFrame;
            public readonly ReadOnlyCollection<ushort> data;
            public readonly Device device;

            public FrameReceivedEventArgs(int lastFrame, int currentFrame, ushort[] data, Device device)
            {
                this.lastFrame = lastFrame;
                this.currentFrame = currentFrame;
                this.data = Array.AsReadOnly(data);
                this.device = device;
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public event EventHandler<FrameReceivedEventArgs> FrameReceived;

        private readonly Options options;
        private TcpClient client;
        private CancellationTokenSource source;

        public Client(Options options) { this.options = options; }

        public async Task<int> Start()
        {
            IPAddress ipAddress = IPAddress.Parse(options.IPAddress);
            logger.Info($"Connecting to {ipAddress}:{options.Port}");
            using (client = new TcpClient())
            using (source = new CancellationTokenSource())
            {
                try
                {
                    await client.ConnectAsync(ipAddress, options.Port);
                    using (NetworkStream stream = client.GetStream())
                    {
                        logger.Info($"Connected to {client.Client.RemoteEndPoint} from {client.Client.LocalEndPoint}");
                        byte[] request = new byte[Math.Max(1, options.Paths.Sum(path => path.Length) + options.Paths.Count())];
                        request[0] = (byte)(request.Length - 1);
                        int byteIndex = 1;
                        foreach (string path in options.Paths) { byteIndex += Encoding.ASCII.GetBytes(path, 0, path.Length, request, byteIndex) + 1; }
                        if (options.Paths.Count() == 0) { logger.Info("Requesting all reachable paths"); }
                        else { logger.Info($"Requesting paths:\n\t{String.Join("\n\t", options.Paths)}"); }
                        await stream.WriteAsync(request, 0, request.Length, source.Token);
                        byte[] buffer = new byte[2];
                        int received = 0;
                        while ((received += await stream.ReadAsync(buffer, 0, 2, source.Token)) < 2) { }
                        buffer = new byte[BitConverter.ToUInt16(buffer, 0)];
                        if (buffer.Length > 0)
                        {
                            received = 0;
                            while ((received += await stream.ReadAsync(buffer, received, buffer.Length - received, source.Token)) < buffer.Length) { }
                        }
                        List<Device> devices = new List<Device>();
                        List<byte[]> deviceOffsets = new List<byte[]>();
                        List<byte[]> deviceBuffers = new List<byte[]>();
                        int parsed = 0;
                        while (parsed < buffer.Length)
                        {
                            string path = Encoding.ASCII.GetString(buffer, parsed, Array.IndexOf(buffer, (byte)0, parsed) - parsed);
                            parsed += path.Length + 1;
                            string description = Encoding.ASCII.GetString(buffer, parsed, Array.IndexOf(buffer, (byte)0, parsed) - parsed);
                            parsed += description.Length + 1;
                            float frequency = BitConverter.ToSingle(buffer, parsed);
                            parsed += 4;
                            List<Source> sources = new List<Source>();
                            List<byte> offsets = new List<byte>();
                            for (int end = buffer[parsed++] * 16 + parsed; parsed < end;)
                            {
                                int port = BitConverter.ToInt32(buffer, parsed);
                                parsed += 4;
                                int frequencyDivisor = BitConverter.ToInt32(buffer, parsed);
                                parsed += 4;
                                int resolution = BitConverter.ToInt32(buffer, parsed);
                                parsed += 4;
                                int channelMask = BitConverter.ToInt32(buffer, parsed);
                                parsed += 4;
                                sources.Add(new Source(port, frequencyDivisor, resolution, channelMask));
                                byte offset = (byte)(resolution / 8);
                                while (channelMask != 0)
                                {
                                    if ((channelMask & 1) == 1) { offsets.Add(offset); }
                                    channelMask >>= 1;
                                }
                            }
                            devices.Add(new Device(path, description, frequency, sources));
                            deviceOffsets.Add(offsets.ToArray());
                            deviceBuffers.Add(new byte[offsets.Sum(offset => offset)]);
                        }
                        if (devices.Count == 0) { logger.Info("Received response with no devices"); }
                        else
                        {
                            StringBuilder message = new StringBuilder("Received response with devices:");
                            foreach (Device device in devices)
                            {
                                message.Append($"\n\ton {device.path} with description: {device.description}, frequency: {device.frequency} and {(device.sources.Count == 0 ? "no sources" : "sources:")}");
                                foreach (Source source in device.sources) { message.Append($"\n\t\tport = {source.port}, frequencyDivisor = {source.frequencyDivisor}, resolution = {source.resolution}, channelMask = {source.channelMask}"); }
                            }
                            logger.Info(message);
                        }
                        if (options.Paths.Count() == 0) { if (devices.Count == 0) { return 0; } }
                        else if (!options.Paths.SequenceEqual(devices.Select(device => device.path)))
                        {
                            logger.Error("Received wrong devices");
                            return 1;
                        }
                        int lastFrame = -1;
                        byte[] header = new byte[5];
                        while (!source.IsCancellationRequested)
                        {
                            received = 0;
                            while ((received += await stream.ReadAsync(header, 0, header.Length, source.Token)) < header.Length) { }
                            byte deviceIndex = header[0];
                            int currentFrame = BitConverter.ToInt32(header, 1);
                            byte[] offsets = deviceOffsets[deviceIndex];
                            buffer = deviceBuffers[deviceIndex];
                            ushort[] data = new ushort[offsets.Length];
                            if (buffer.Length > 0)
                            {
                                received = 0;
                                while ((received += await stream.ReadAsync(buffer, 0, buffer.Length, source.Token)) < buffer.Length) { }
                                byteIndex = 0;
                                for (int index = 0; index < offsets.Length; byteIndex += offsets[index], index++)
                                {
                                    if (offsets[index] == 1) { data[index] = buffer[byteIndex]; }
                                    else { data[index] = BitConverter.ToUInt16(buffer, byteIndex); }
                                }
                            }
                            Device device = devices[deviceIndex];
                            FrameReceived?.Invoke(this, new FrameReceivedEventArgs(lastFrame, currentFrame, data, device));
                            int missing = currentFrame - lastFrame;
                            if (missing > 1) { logger.Warn($"Device on {device.path} dropped {missing - 1} frames"); }
                            lastFrame = currentFrame;
                        }
                    }
                }
                catch (ObjectDisposedException) { if (!source.IsCancellationRequested) throw; }
                catch (NullReferenceException) { if (!source.IsCancellationRequested) throw; }
            }
            logger.Info("Cleaning up");
            client = null;
            source = null;
            logger.Info("Shutting down");
            return 0;
        }

        public void Stop()
        {
            logger.Info("Stopping");
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            client?.Close();
        }
    }
}
