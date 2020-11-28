using System;
using System.Text;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;

namespace PluxAdapter.Servers
{
    /// <summary>
    /// Handles <see cref="PluxAdapter.Clients.Client" /> connected to <see cref="PluxAdapter.Servers.Server" />, negotiates requests and transfers raw data.
    /// </summary>
    public sealed class Handler
    {
        /// <summary>
        /// Holder of internal state required for raw data transfer by <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        private sealed class Cache
        {
            /// <summary>
            /// Raw data <see cref="byte" /> offsets in <see cref="PluxAdapter.Servers.Handler.Cache.buffer" />.
            /// </summary>
            public readonly byte[] offsets;
            /// <summary>
            /// Transfer buffer for raw data of particular <see cref="PluxAdapter.Servers.Device" />.
            /// </summary>
            public readonly byte[] buffer;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Servers.Handler.Cache" />.
            /// </summary>
            /// <param name="index"><see cref="PluxAdapter.Servers.Device" /> index as requested by <see cref="PluxAdapter.Clients.Client" />.</param>
            /// <param name="offsets">Raw data <see cref="byte" /> offsets in <see cref="PluxAdapter.Servers.Handler.Cache.buffer" />.</param>
            public Cache(byte index, byte[] offsets)
            {
                this.offsets = offsets;
                // allocate room for device index, frame counter and raw data
                this.buffer = new byte[offsets.Sum(offset => offset) + 5];
                // write device index, note that this is static for each cache
                buffer[0] = index;
            }
        }

        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Transfer buffer <see cref="PluxAdapter.Servers.Handler.Cache" /> mapped to <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private readonly Dictionary<Device, Cache> devices = new Dictionary<Device, Cache>();
        /// <summary>
        /// <see cref="PluxAdapter.Servers.Server" /> managing <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        private readonly Server server;
        /// <summary>
        /// Underlying connection.
        /// </summary>
        private readonly TcpClient client;
        /// <summary>
        /// <see cref="System.Threading.CancellationToken" /> to monitor.
        /// </summary>
        private readonly CancellationToken token;
        /// <summary>
        /// Underlying connection stream.
        /// </summary>
        private readonly NetworkStream stream;

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Servers.Handler" />.
        /// </summary>
        /// <param name="server"><see cref="PluxAdapter.Servers.Server" /> managing <see cref="PluxAdapter.Servers.Handler" />.</param>
        /// <param name="client">Underlying connection.</param>
        /// <param name="token"><see cref="System.Threading.CancellationToken" /> to monitor.</param>
        public Handler(Server server, TcpClient client, CancellationToken token)
        {
            this.server = server;
            this.client = client;
            this.token = token;
            this.stream = client.GetStream();
        }

        /// <summary>
        /// Event callback of <see cref="PluxAdapter.Servers.Device.FrameReceived" />. Sends raw data to <see cref="PluxAdapter.Servers.Handler.client" /> connection.
        /// </summary>
        /// <param name="sender"><see cref="PluxAdapter.Servers.Device" /> distributing raw data.</param>
        /// <param name="eventArgs"><see cref="PluxAdapter.Servers.Device.FrameReceivedEventArgs" /> containing event data.</param>
        private void SendFrame(object sender, Device.FrameReceivedEventArgs eventArgs)
        {
            // note that this method is called from device specific loop, therefore overwriting device specific buffer is ok
            try
            {
                // grab device cache and write frame counter
                Cache cache;
                lock (devices) { cache = devices[sender as Device]; }
                Buffer.BlockCopy(BitConverter.GetBytes(eventArgs.currentFrame), 0, cache.buffer, 1, 4);
                // loop over offsets while advancing buffer cursor
                int byteIndex = 5;
                for (int index = 0; index < cache.offsets.Length; byteIndex += cache.offsets[index], index++)
                {
                    // write raw data as byte or ushort
                    if (cache.offsets[index] == 1) { cache.buffer[byteIndex] = (byte)eventArgs.data[index]; }
                    else { Buffer.BlockCopy(BitConverter.GetBytes((ushort)eventArgs.data[index]), 0, cache.buffer, byteIndex, 2); }
                }
                // send to client
                lock (stream) { stream.Write(cache.buffer, 0, cache.buffer.Length); }
            }
            catch (ObjectDisposedException) { Stop(); if (!token.IsCancellationRequested) throw; }
            catch (NullReferenceException) { Stop(); if (!token.IsCancellationRequested) throw; }
            catch (System.IO.IOException) { logger.Info("Connection closed by client during transfer"); Stop(); }
            catch (Exception) { Stop(); throw; }
        }

        /// <summary>
        /// Negotiates requested and available <see cref="PluxAdapter.Servers.Device" /> and registers <see cref="PluxAdapter.Servers.Device.FrameReceived" /> event handlers.
        /// </summary>
        /// <returns><see cref="System.Threading.Tasks.Task" /> representing negotiation.</returns>
        public async Task Start()
        {
            try
            {
                logger.Info($"Accepted connection from {client.Client.RemoteEndPoint} to {client.Client.LocalEndPoint}");
                // receive request length as single byte and use that to receive request itself
                byte[] buffer = await stream.ReadAllAsync((await stream.ReadAllAsync(1, token))[0], token);
                byte[] response;
                // decode requested paths
                string[] paths = Encoding.ASCII.GetString(buffer, 0, buffer.Length).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                // keep devices in order requested since that determines client specific device index
                List<KeyValuePair<Device, List<PluxDotNet.Source>>> sortedDevices = new List<KeyValuePair<Device, List<PluxDotNet.Source>>>();
                IEnumerable<Device> requestedDevices;
                if (paths.Length == 0)
                {
                    logger.Info("Received request for all reachable paths");
                    // nothing specific requested, scan for anything new and return all we got
                    server.manager.Scan("");
                    requestedDevices = server.manager.Devices.Values;
                }
                else
                {
                    logger.Info($"Received request for paths:\n\t{String.Join("\n\t", paths)}");
                    // try to get all requested devices, note that we may not be able to acquire them all, doesn't bother us, client might though
                    requestedDevices = paths.Select(path => server.manager.Get(path)).Where(device => !(device is null));
                }
                // loop requested devices and their sources
                lock (devices)
                {
                    byte deviceCounter = 0;
                    foreach (Device device in requestedDevices)
                    {
                        List<PluxDotNet.Source> sources = device.Sources;
                        List<byte> offsets = new List<byte>();
                        foreach (PluxDotNet.Source source in sources)
                        {
                            // add offset for each channel used
                            byte offset = (byte)(source.nBits / 8);
                            for (int channelMask = source.chMask; channelMask != 0; channelMask >>= 1) { if ((channelMask & 1) == 1) { offsets.Add(offset); } }
                        }
                        // add device and it's cache
                        sortedDevices.Add(new KeyValuePair<Device, List<PluxDotNet.Source>>(device, sources));
                        devices.Add(device, new Cache(deviceCounter++, offsets.ToArray()));
                    }
                }
                // allocate response buffer and write it's length
                response = new byte[sortedDevices.Sum(kvp => kvp.Key.path.Length + kvp.Key.Description.Length + kvp.Value.Count * 16) + sortedDevices.Count * 7 + 2];
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)(response.Length - 2)), 0, response, 0, 2);
                // loop devices and their sources in order while advancing buffer cursor
                int byteIndex = 2;
                foreach (KeyValuePair<Device, List<PluxDotNet.Source>> kvp in sortedDevices)
                {
                    // encode device path, description, frequency and source count
                    byteIndex += Encoding.ASCII.GetBytes(kvp.Key.path, 0, kvp.Key.path.Length, response, byteIndex) + 1;
                    byteIndex += Encoding.ASCII.GetBytes(kvp.Key.Description, 0, kvp.Key.Description.Length, response, byteIndex) + 1;
                    Buffer.BlockCopy(BitConverter.GetBytes(kvp.Key.frequency), 0, response, byteIndex, 4);
                    byteIndex += 4;
                    response[byteIndex++] = (byte)kvp.Value.Count;
                    foreach (PluxDotNet.Source source in kvp.Value)
                    {
                        // encode source port, freqDivisor, nBits and chMask
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
                // log response
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
                // send to client and register callback
                await stream.WriteAsync(response, 0, response.Length, token);
                lock (devices) { foreach (Device device in devices.Keys) { device.FrameReceived += SendFrame; } }
            }
            catch (ObjectDisposedException) { Stop(); if (!token.IsCancellationRequested) throw; }
            catch (NullReferenceException) { Stop(); if (!token.IsCancellationRequested) throw; }
            catch (System.IO.IOException) { logger.Warn("Connection closed by client during negotiation"); Stop(); }
            catch (Exception) { Stop(); throw; }
        }

        /// <summary>
        /// Closes <see cref="PluxAdapter.Servers.Handler.client" /> connection and unregisters <see cref="PluxAdapter.Servers.Device.FrameReceived" /> event handlers. This is threadsafe.
        /// </summary>
        public void Stop()
        {
            // note that log may fail if connection was already closed
            try { logger.Info($"Stopping connection from {client.Client.RemoteEndPoint} to {client.Client.LocalEndPoint}"); }
            catch (ObjectDisposedException) { }
            // close connection and unregister callback
            client.Close();
            lock (devices)
            {
                foreach (Device device in devices.Keys) { device.FrameReceived -= SendFrame; }
                devices.Clear();
            }
        }
    }
}

