using System;
using System.Text;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;

namespace PluxAdapter
{
    public sealed class Handler
    {
        private sealed class Cache
        {
            public readonly byte[] offsets;
            public readonly byte[] buffer;

            public Cache(byte index, byte[] offsets)
            {
                this.offsets = offsets;
                this.buffer = new byte[offsets.Sum(offset => offset) + 5];
                buffer[0] = index;
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Device, Cache> devices = new Dictionary<Device, Cache>();
        private readonly Server server;
        private readonly TcpClient client;
        private readonly CancellationToken token;
        private readonly NetworkStream stream;

        public Handler(Server server, TcpClient client, CancellationToken token)
        {
            this.server = server;
            this.client = client;
            this.token = token;
            this.stream = client.GetStream();
        }

        private void SendFrame(object sender, Device.FrameReceivedEventArgs eventArgs)
        {
            Cache cache;
            lock (devices) { cache = devices[sender as Device]; }
            Buffer.BlockCopy(BitConverter.GetBytes(eventArgs.currentFrame), 0, cache.buffer, 1, 4);
            int byteIndex = 5;
            for (int index = 0; index < cache.offsets.Length; byteIndex += cache.offsets[index], index++)
            {
                if (cache.offsets[index] == 1) { cache.buffer[byteIndex] = (byte)eventArgs.data[index]; }
                else { Buffer.BlockCopy(BitConverter.GetBytes((ushort)eventArgs.data[index]), 0, cache.buffer, byteIndex, 2); }
            }
            try { lock (stream) { stream.Write(cache.buffer, 0, cache.buffer.Length); } }
            catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
            catch (NullReferenceException) { if (!token.IsCancellationRequested) throw; }
            catch (System.IO.IOException)
            {
                logger.Info("Connection closed by client during transfer");
                Stop();
            }
        }

        public async Task Start()
        {
            try
            {
                logger.Info($"Accepted connection from {client.Client.RemoteEndPoint} to {client.Client.LocalEndPoint}");
                byte[] buffer = new byte[1];
                while (await stream.ReadAsync(buffer, 0, 1, token) < 1) { }
                buffer = new byte[buffer[0]];
                if (buffer.Length > 0)
                {
                    int received = 0;
                    while ((received += await stream.ReadAsync(buffer, received, buffer.Length - received, token)) < buffer.Length) { }
                }
                byte[] response;
                string[] paths = Encoding.ASCII.GetString(buffer, 0, buffer.Length).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                List<KeyValuePair<Device, List<PluxDotNet.Source>>> sortedDevices = new List<KeyValuePair<Device, List<PluxDotNet.Source>>>();
                IEnumerable<Device> requestedDevices;
                if (paths.Length == 0)
                {
                    logger.Info("Received request for all reachable paths");
                    server.manager.Scan("");
                    requestedDevices = server.manager.Devices.Values;
                }
                else
                {
                    logger.Info($"Received request for paths:\n\t{String.Join("\n\t", paths)}");
                    requestedDevices = paths.Select(path => server.manager.Get(path));
                }
                lock (devices)
                {
                    byte deviceCounter = 0;
                    foreach (Device device in requestedDevices)
                    {
                        List<PluxDotNet.Source> sources = device.Sources;
                        List<byte> offsets = new List<byte>();
                        foreach (PluxDotNet.Source source in sources)
                        {
                            byte offset = (byte)(source.nBits / 8);
                            for (int channelMask = source.chMask; channelMask != 0; channelMask >>= 1) { if ((channelMask & 1) == 1) { offsets.Add(offset); } }
                        }
                        sortedDevices.Add(new KeyValuePair<Device, List<PluxDotNet.Source>>(device, sources));
                        devices.Add(device, new Cache(deviceCounter++, offsets.ToArray()));
                    }
                }
                response = new byte[sortedDevices.Sum(kvp => kvp.Key.path.Length + kvp.Key.Description.Length + kvp.Value.Count * 16) + sortedDevices.Count * 7 + 2];
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)(response.Length - 2)), 0, response, 0, 2);
                int byteIndex = 2;
                foreach (KeyValuePair<Device, List<PluxDotNet.Source>> kvp in sortedDevices)
                {
                    byteIndex += Encoding.ASCII.GetBytes(kvp.Key.path, 0, kvp.Key.path.Length, response, byteIndex) + 1;
                    byteIndex += Encoding.ASCII.GetBytes(kvp.Key.Description, 0, kvp.Key.Description.Length, response, byteIndex) + 1;
                    Buffer.BlockCopy(BitConverter.GetBytes(kvp.Key.frequency), 0, response, byteIndex, 4);
                    byteIndex += 4;
                    response[byteIndex++] = (byte)kvp.Value.Count;
                    foreach (PluxDotNet.Source source in kvp.Value)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(source.port), 0, response, byteIndex, 4);
                        byteIndex += 4;
                        Buffer.BlockCopy(BitConverter.GetBytes(source.freqDivisor), 0, response, byteIndex, 4);
                        byteIndex += 4;
                        Buffer.BlockCopy(BitConverter.GetBytes(source.nBits), 0, response, byteIndex, 4);
                        byteIndex += 4;
                        Buffer.BlockCopy(BitConverter.GetBytes(source.chMask), 0, response, byteIndex, 4);
                        byteIndex += 4;
                    }
                }
                if (sortedDevices.Count == 0) { logger.Info("Responding with no devices"); }
                else
                {
                    StringBuilder message = new StringBuilder("Responding with devices:");
                    foreach (KeyValuePair<Device, List<PluxDotNet.Source>> kvp in sortedDevices)
                    {
                        message.Append($"\n\ton {kvp.Key.path} with description: {kvp.Key.Description}, frequency: {kvp.Key.frequency} and {(kvp.Value.Count == 0 ? "no sources" : "sources:")}");
                        foreach (PluxDotNet.Source source in kvp.Value) { message.Append($"\n\t\tport = {source.port}, frequencyDivisor = {source.freqDivisor}, resolution = {source.nBits}, channelMask = {source.chMask}"); }
                    }
                    logger.Info(message);
                }
                await stream.WriteAsync(response, 0, response.Length, token);
                lock (devices) { foreach (Device device in devices.Keys) { device.FrameReceived += SendFrame; } }
            }
            catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
            catch (NullReferenceException) { if (!token.IsCancellationRequested) throw; }
            catch (System.IO.IOException)
            {
                logger.Info("Connection closed by client during negotiation");
                Stop();
            }
        }

        public void Stop()
        {
            try { logger.Info($"Stopping connection from {client.Client.RemoteEndPoint} to {client.Client.LocalEndPoint}"); }
            catch (ObjectDisposedException) { }
            client.Close();
            lock (devices)
            {
                foreach (Device device in devices.Keys) { device.FrameReceived -= SendFrame; }
                devices.Clear();
            }
        }
    }
}

