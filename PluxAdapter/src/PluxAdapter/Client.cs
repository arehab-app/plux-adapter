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
    /// <summary>
    /// Connects to <see cref="PluxAdapter.Server" /> and streams raw data from <see cref="PluxAdapter.Client.FrameReceived" /> event.
    /// </summary>
    public sealed class Client : Program.IExecutable
    {
        /// <summary>
        /// <see cref="PluxAdapter.Client" /> configuration.
        /// </summary>
        [Verb("client", HelpText = "Start client.")]
        public sealed class Options
        {
            /// <summary>
            /// IP to connect to.
            /// </summary>
            [Option("ip-address", Default = "127.0.0.1", HelpText = "IP to connect to.")]
            public string IPAddress { get; }

            /// <summary>
            /// Port to connect to.
            /// </summary>
            [Option("port", Default = 24242, HelpText = "Port to connect to.")]
            public int Port { get; }

            /// <summary>
            /// Paths of devices to request.
            /// </summary>
            [Option("paths", HelpText = "(Default: all reachable paths) Paths of devices to request.")]
            public IEnumerable<string> Paths { get; }

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Client.Options" />.
            /// </summary>
            /// <param name="ipAddress">IP to connect to.</param>
            /// <param name="port">Port to connect to.</param>
            /// <param name="paths">Paths of devices to request.</param>
            public Options(string ipAddress, int port, IEnumerable<string> paths)
            {
                IPAddress = ipAddress;
                Port = port;
                Paths = paths.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Mirror of <see cref="PluxAdapter.Device" /> configuration on <see cref="PluxAdapter.Server" /> side.
        /// </summary>
        public sealed class Device
        {
            /// <summary>
            /// Path to <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            public readonly string path;
            /// <summary>
            /// Description of <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            public readonly string description;
            /// <summary>
            /// Connection base frequency for <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            public readonly float frequency;
            /// <summary>
            /// <see cref="PluxAdapter.Client.Source" /> providing data to <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            public readonly ReadOnlyCollection<Source> sources;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            /// <param name="path">Path to <see cref="PluxAdapter.Client.Device" />.</param>
            /// <param name="description">Description of <see cref="PluxAdapter.Client.Device" />.</param>
            /// <param name="frequency">Connection base frequency for <see cref="PluxAdapter.Client.Device" />.</param>
            /// <param name="sources"><see cref="PluxAdapter.Client.Source" /> providing data to <see cref="PluxAdapter.Client.Device" />.</param>
            public Device(string path, string description, float frequency, List<Source> sources)
            {
                this.path = path;
                this.description = description;
                this.frequency = frequency;
                this.sources = sources.AsReadOnly();
            }
        }

        /// <summary>
        /// Mirror of <see cref="PluxDotNet.Source" /> configuration on <see cref="PluxAdapter.Server" /> side.
        /// </summary>
        public sealed class Source
        {
            /// <summary>
            /// Raw data port on <see cref="PluxAdapter.Client.Device" />.
            /// </summary>
            public readonly int port;
            /// <summary>
            /// Divisor applied to <see cref="PluxAdapter.Client.Device.frequency" />.
            /// </summary>
            public readonly int frequencyDivisor;
            /// <summary>
            /// Raw data resolution in bits.
            /// </summary>
            public readonly int resolution;
            /// <summary>
            /// Mask of raw data channels open on <see cref="PluxAdapter.Client.Source.port" />.
            /// </summary>
            public readonly int channelMask;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Client.Source" />.
            /// </summary>
            /// <param name="port">Raw data port on <see cref="PluxAdapter.Client.Device" />.</param>
            /// <param name="frequencyDivisor">Divisor applied to <see cref="PluxAdapter.Client.Device.frequency" />.</param>
            /// <param name="resolution">Raw data resolution in bits.</param>
            /// <param name="channelMask">Mask of raw data channels open on <paramref name="port" />.</param>
            public Source(int port, int frequencyDivisor, int resolution, int channelMask)
            {
                this.port = port;
                this.frequencyDivisor = frequencyDivisor;
                this.resolution = resolution;
                this.channelMask = channelMask;
            }
        }

        /// <summary>
        /// Event data for <see cref="PluxAdapter.Client.FrameReceived" />.
        /// </summary>
        public sealed class FrameReceivedEventArgs : EventArgs
        {
            /// <summary>
            /// Counter of last frame received by <see cref="PluxAdapter.Client" />.
            /// </summary>
            public readonly int lastFrame;
            /// <summary>
            /// Counter of this frame.
            /// </summary>
            public readonly int currentFrame;
            /// <summary>
            /// Raw data from <see cref="PluxAdapter.Client" />.
            /// </summary>
            public readonly ReadOnlyCollection<ushort> data;
            /// <summary>
            /// <see cref="PluxAdapter.Client.Device" /> mirroring <see cref="PluxAdapter.Device" /> configuration on <see cref="PluxAdapter.Server" /> side.
            /// </summary>
            public readonly Device device;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Client.FrameReceivedEventArgs" />.
            /// </summary>
            /// <param name="lastFrame">Counter of last frame received by <see cref="PluxAdapter.Client" />.</param>
            /// <param name="currentFrame">Counter of this frame.</param>
            /// <param name="data">Raw data.</param>
            /// <param name="device"><see cref="PluxAdapter.Client.Device" /> mirroring <see cref="PluxAdapter.Device" /> configuration on <see cref="PluxAdapter.Server" /> side.</param>
            public FrameReceivedEventArgs(int lastFrame, int currentFrame, ushort[] data, Device device)
            {
                this.lastFrame = lastFrame;
                this.currentFrame = currentFrame;
                this.data = Array.AsReadOnly(data);
                this.device = device;
            }
        }

        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Client" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Event raised on each raw data frame received.
        /// </summary>
        public event EventHandler<FrameReceivedEventArgs> FrameReceived;

        /// <summary>
        /// Underlying connection.
        /// </summary>
        private TcpClient client;
        /// <summary>
        /// <see cref="System.Threading.CancellationTokenSource" /> monitored by <see cref="PluxAdapter.Client" />.
        /// </summary>
        private CancellationTokenSource source;

        /// <summary>
        /// Configuration options.
        /// </summary>
        public readonly Options options;

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Client" /> with <see cref="PluxAdapter.Client.Options" />.
        /// </summary>
        /// <param name="options">Configuration options.</param>
        public Client(Options options) { this.options = options; }

        /// <summary>
        /// Runs <see cref="PluxAdapter.Client" /> communication loop.
        /// </summary>
        /// <returns><see cref="int" /> indicating communication loop exit reason.</returns>
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
                        byte[] buffer = await stream.ReadAllAsync(BitConverter.ToUInt16(await stream.ReadAllAsync(2, source.Token), 0), source.Token);
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
                            await stream.ReadAllAsync(header, source.Token);
                            byte deviceIndex = header[0];
                            int currentFrame = BitConverter.ToInt32(header, 1);
                            byte[] offsets = deviceOffsets[deviceIndex];
                            buffer = deviceBuffers[deviceIndex];
                            ushort[] data = new ushort[offsets.Length];
                            await stream.ReadAllAsync(buffer, source.Token);
                            byteIndex = 0;
                            for (int index = 0; index < offsets.Length; byteIndex += offsets[index], index++)
                            {
                                if (offsets[index] == 1) { data[index] = buffer[byteIndex]; }
                                else { data[index] = BitConverter.ToUInt16(buffer, byteIndex); }
                            }
                            Device device = devices[deviceIndex];
                            FrameReceived?.Invoke(this, new FrameReceivedEventArgs(lastFrame, currentFrame, data, device));
                            int missing = currentFrame - lastFrame;
                            if (missing > 1) { logger.Warn($"Device on {device.path} dropped {missing - 1} frames"); }
                            lastFrame = currentFrame;
                        }
                    }
                }
                catch (ObjectDisposedException) { Stop(); if (!source.IsCancellationRequested) throw; }
                catch (NullReferenceException) { Stop(); if (!source.IsCancellationRequested) throw; }
                catch (System.IO.IOException) { logger.Error("Connection closed by server"); Stop(); }
                catch (Exception) { Stop(); throw; }
            }
            logger.Info("Cleaning up");
            client = null;
            source = null;
            logger.Info("Shutting down");
            return 0;
        }

        /// <summary>
        /// Stops <see cref="PluxAdapter.Client" /> communication loop. This is threadsafe.
        /// </summary>
        public void Stop()
        {
            logger.Info("Stopping");
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            client?.Close();
        }
    }
}
