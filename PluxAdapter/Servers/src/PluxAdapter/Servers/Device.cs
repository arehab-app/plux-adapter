using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using NLog;

namespace PluxAdapter.Servers
{
    /// <summary>
    /// Manages connection to and distributes raw data from <see cref="PluxAdapter.Servers.Device.Plux" />.
    /// </summary>
    public sealed class Device
    {
        /// <summary>
        /// Extension of <see cref="PluxDotNet.SignalsDev" /> communicating with <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private sealed class Plux : PluxDotNet.SignalsDev
        {
            /// <summary>
            /// <see cref="PluxAdapter.Servers.Device" /> managing <see cref="PluxAdapter.Servers.Device.Plux" />.
            /// </summary>
            private readonly Device device;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Servers.Device.Plux" /> on <paramref name="path" /> managed by <paramref name="device" />.
            /// </summary>
            /// <param name="path"><see cref="PluxAdapter.Servers.Device" /> managing <see cref="PluxAdapter.Servers.Device.Plux" />.</param>
            /// <param name="device">Path to <see cref="PluxAdapter.Servers.Device.Plux" />.</param>
            public Plux(string path, Device device) : base(path) { this.device = device; }

            /// <summary>
            /// Frame callback called by <see cref="PluxDotNet.BaseDev.Loop" />.
            /// </summary>
            /// <param name="currentFrame">Counter of this frame.</param>
            /// <param name="data">Raw data.</param>
            /// <returns>Indicator if <see cref="PluxDotNet.BaseDev.Loop" /> should stop.</returns>
            public override bool OnRawFrame(int currentFrame, int[] data)
            {
                // forward raw data to device
                device.OnRawFrame(currentFrame, data);
                return device.source.IsCancellationRequested;
            }

            /// <summary>
            /// Interrupt callback called by <see cref="PluxDotNet.BaseDev.Loop" />.
            /// </summary>
            /// <param name="args"><see cref="object" /> passed to <see cref="PluxDotNet.BaseDev.Interrupt(object)" />.</param>
            /// <returns>Indicator if <see cref="PluxDotNet.BaseDev.Loop" /> should stop.</returns>
            public override bool OnInterrupt(object args)
            {
                return device.source.IsCancellationRequested;
            }
        }

        /// <summary>
        /// Event data for <see cref="PluxAdapter.Servers.Device.FrameReceived" />.
        /// </summary>
        public sealed class FrameReceivedEventArgs : EventArgs
        {
            /// <summary>
            /// Counter of last frame received by <see cref="PluxAdapter.Servers.Device" />.
            /// </summary>
            public readonly int lastFrame;
            /// <summary>
            /// Counter of this frame.
            /// </summary>
            public readonly int currentFrame;
            /// <summary>
            /// Raw data from <see cref="PluxAdapter.Servers.Device" />.
            /// </summary>
            public readonly ReadOnlyCollection<int> data;

            /// <summary>
            /// Creates new <see cref="PluxAdapter.Servers.Device.FrameReceivedEventArgs" />.
            /// </summary>
            /// <param name="lastFrame">Counter of last frame received by <see cref="PluxAdapter.Servers.Device" />.</param>
            /// <param name="currentFrame">Counter of this frame.</param>
            /// <param name="data">Raw data.</param>
            public FrameReceivedEventArgs(int lastFrame, int currentFrame, int[] data)
            {
                this.lastFrame = lastFrame;
                this.currentFrame = currentFrame;
                this.data = Array.AsReadOnly(data);
            }
        }

        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Ticks at epoch.
        /// </summary>
        private static readonly long epoch = new DateTime(1970, 1, 1).Ticks;
        /// <summary>
        /// Directory to write csv to.
        /// </summary>
        private static readonly string dataDirectory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "data")).FullName;

        /// <summary>
        /// Event raised on each raw data frame received.
        /// </summary>
        public event EventHandler<FrameReceivedEventArgs> FrameReceived;

        /// <summary>
        /// <see cref="PluxDotNet.Source" /> providing data to <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private readonly List<PluxDotNet.Source> sources = new List<PluxDotNet.Source>();
        /// <summary>
        /// <see cref="PluxAdapter.Servers.Manager" /> managing <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private readonly Manager manager;
        /// <summary>
        /// Counter of last raw data frame received.
        /// </summary>
        private int lastFrame = -1;
        /// <summary>
        /// <see cref="System.Threading.CancellationTokenSource" /> monitored by <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        private CancellationTokenSource source;
        /// <summary>
        /// Underlying connection.
        /// </summary>
        private Plux plux;
        /// <summary>
        /// File to write csv to.
        /// </summary>
        private StreamWriter csv;

        /// <summary>
        /// Path <see cref="PluxAdapter.Servers.Device" /> is located on.
        /// </summary>
        public readonly string path;
        /// <summary>
        /// Connection base frequency for <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        public readonly float frequency;

        /// <summary>
        /// Description of <see cref="PluxAdapter.Servers.Device" />.
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// <see cref="PluxDotNet.Source" /> providing data to <see cref="PluxAdapter.Servers.Device" />. This is threadsafe.
        /// </summary>
        public List<PluxDotNet.Source> Sources
        {
            get
            {
                lock (sources)
                {
                    List<PluxDotNet.Source> copy = new List<PluxDotNet.Source>(sources.Count);
                    foreach (PluxDotNet.Source source in sources)
                    {
                        copy.Add(new PluxDotNet.Source { port = source.port, freqDivisor = source.freqDivisor, nBits = source.nBits, chMask = source.chMask });
                    }
                    return copy;
                }
            }
        }

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Servers.Device" /> on <paramref name="path" /> managed by <paramref name="manager" />.
        /// </summary>
        /// <param name="manager"><see cref="PluxAdapter.Servers.Manager" /> managing <see cref="PluxAdapter.Servers.Device" />.</param>
        /// <param name="path">Path to <see cref="PluxAdapter.Servers.Device" />.</param>
        public Device(Manager manager, string path)
        {
            this.manager = manager;
            this.path = path;
            this.frequency = manager.frequency;
        }

        /// <summary>
        /// Receives and distributes data from <see cref="PluxAdapter.Servers.Device.plux" />.
        /// </summary>
        /// <param name="currentFrame">Frame counter.</param>
        /// <param name="data">Raw data.</param>
        private void OnRawFrame(int currentFrame, int[] data)
        {
            // distribute raw data
            FrameReceivedEventArgs eventArgs = new FrameReceivedEventArgs(lastFrame, currentFrame, data);
            FrameReceived?.Invoke(this, eventArgs);
            // write to csv
            csv.WriteLine($"{currentFrame},{DateTime.Now.Ticks - epoch},{String.Join(",", data)}");
            // log missing frames
            int missing = currentFrame - lastFrame;
            if (missing > 1) { logger.Warn($"Device on {path} dropped {missing - 1} frames"); }
            lastFrame = currentFrame;
            // log raw data
            // if (eventArgs.data.Count == 0) { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {path} with no data"); }
            // else { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {path} with data: {String.Join(" ", eventArgs.data)}"); }
        }

        /// <summary>
        /// Connects to and configures <see cref="PluxAdapter.Servers.Device" />. This must be called before <see cref="PluxAdapter.Servers.Device.Start" />.
        /// </summary>
        public void Connect()
        {
            // connect to underlying device and log it's properties
            plux = new Plux(path, this);
            StringBuilder message = new StringBuilder($"Connected to device on {path} with properties:");
            Dictionary<string, object> properties = plux.GetProperties();
            foreach (KeyValuePair<string, object> kvp in properties) { message.Append($"\n\t{kvp.Key} = {kvp.Value}"); }
            logger.Info(message);
            // need description to know how to configure it
            if (!properties.ContainsKey("description"))
            {
                logger.Warn($"Device on {path} has no description");
                return;
            }
            // got description, grab and switch on it
            Description = properties["description"].ToString();
            lock (sources)
            {
                switch (Description)
                {
                    case "biosignalsplux":
                        sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 3, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 4, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 5, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 6, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 7, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 8, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        break;
                    case "MuscleBAN BE Plux":
                        sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.resolution, chMask = 7 });
                        break;
                    case "OpenBANPlux":
                        sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.resolution, chMask = 1 });
                        sources.Add(new PluxDotNet.Source { port = 11, freqDivisor = 1, nBits = manager.resolution, chMask = 7 });
                        break;
                    default:
                        logger.Warn($"Device on {path} has unknown description: {Description}");
                        // note that device with no sources works, but it's somewhat useless
                        break;
                }
            }
        }

        /// <summary>
        /// Runs <see cref="PluxAdapter.Servers.Device" /> communication loop. This must be called after <see cref="PluxAdapter.Servers.Device.Connect" />.
        /// </summary>
        public void Start()
        {
            // allocate and fill csv header while logging device configuration
            List<string> header = new List<string>();
            lock (sources)
            {
                StringBuilder message = new StringBuilder($"Starting device on {path} with description: {Description}, frequency: {frequency} and {(sources.Count == 0 ? "no sources" : "sources:")}");
                foreach (PluxDotNet.Source source in sources)
                {
                    // add column header for each open channel on port
                    for (int channel = 0; (source.chMask >> channel) > 0; channel++) { if ((source.chMask & (1 << channel)) > 0) { header.Add($"{source.port}-{channel}"); } }
                    message.Append($"\n\tport = {source.port}, frequencyDivisor = {source.freqDivisor}, resolution = {source.nBits}, channelMask = {source.chMask}");
                }
                logger.Info(message);
            }
            using (plux)
            // open csv file for writing, note that this'll fail if file already exists, but given time resolution used that's very unlikely
            using (csv = new StreamWriter(new FileStream(
                Path.Combine(dataDirectory, $"PluxAdapter.{DateTime.Now:yyyy-MM-dd-HH-mm-ss-ffff}.{String.Join("-", path.Split(Path.GetInvalidFileNameChars()))}.csv"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true), Encoding.ASCII, 4096, false))
            using (source = new CancellationTokenSource())
            {
                // write csv header
                csv.WriteLine($"frame,ticks,{String.Join(",", header)}");
                try
                {
                    // start raw data transfer and enter communication loop
                    plux?.Start(manager.frequency, Sources);
                    plux?.Loop();
                }
                finally { plux?.Stop(); }
            }
            logger.Info("Cleaning up");
            plux = null;
            csv = null;
            source = null;
            lastFrame = -1;
            lock (sources) { sources.Clear(); }
            logger.Info("Shutting down");
        }

        /// <summary>
        /// Stops <see cref="PluxAdapter.Servers.Device" /> communication loop. This is threadsafe.
        /// </summary>
        public void Stop()
        {
            logger.Info($"Stopping device on {path}");
            // always cancel token first
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            // interrupt communication loop
            try { plux?.Interrupt(null); }
            catch (PluxDotNet.Exception.InvalidInstance) { }
            catch (PluxDotNet.Exception.InvalidOperation) { }
        }
    }
}
