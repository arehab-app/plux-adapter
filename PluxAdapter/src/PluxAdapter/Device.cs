using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using NLog;

namespace PluxAdapter
{
    public sealed class Device
    {
        private sealed class Plux : PluxDotNet.SignalsDev
        {
            private readonly Device device;

            public Plux(string path, Device device) : base(path) { this.device = device; }

            public override bool OnRawFrame(int currentFrame, int[] data)
            {
                device.OnRawFrame(currentFrame, data);
                return device.source.IsCancellationRequested;
            }

            public override bool OnInterrupt(object param)
            {
                return device.source.IsCancellationRequested;
            }
        }

        public sealed class FrameReceivedEventArgs : EventArgs
        {
            public readonly int lastFrame;
            public readonly int currentFrame;
            public readonly ReadOnlyCollection<int> data;

            public FrameReceivedEventArgs(int lastFrame, int currentFrame, int[] data)
            {
                this.lastFrame = lastFrame;
                this.currentFrame = currentFrame;
                this.data = Array.AsReadOnly(data);
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public event EventHandler<FrameReceivedEventArgs> FrameReceived;

        private readonly List<PluxDotNet.Source> sources = new List<PluxDotNet.Source>();
        private readonly Manager manager;
        private int lastFrame = -1;
        private CancellationTokenSource source;
        private Plux plux;

        public readonly string path;
        public readonly float frequency;

        public string Description { get; private set; }

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

        public Device(Manager manager, string path)
        {
            this.manager = manager;
            this.path = path;
            this.frequency = manager.frequency;
        }

        private void OnRawFrame(int currentFrame, int[] data)
        {
            FrameReceivedEventArgs eventArgs = new FrameReceivedEventArgs(lastFrame, currentFrame, data);
            FrameReceived?.Invoke(this, eventArgs);
            int missing = currentFrame - lastFrame;
            if (missing > 1) { logger.Warn($"Device on {path} dropped {missing - 1} frames"); }
            lastFrame = currentFrame;
            if (eventArgs.data.Count == 0) { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {path} with no data"); }
            else { logger.Trace($"Received frame {eventArgs.currentFrame} from device on {path} with data: {String.Join(" ", eventArgs.data)}"); }
        }

        public void Connect()
        {
            plux = new Plux(path, this);
            StringBuilder message = new StringBuilder($"Connected to device on {path} with properties:");
            Dictionary<string, object> properties = plux.GetProperties();
            foreach (KeyValuePair<string, object> kvp in properties) { message.Append($"\n\t{kvp.Key} = {kvp.Value}"); }
            logger.Info(message);
            if (!properties.ContainsKey("description"))
            {
                logger.Warn($"Device on {path} has no description");
                return;
            }
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
                        break;
                }
            }
        }

        public void Start()
        {
            lock (sources)
            {
                StringBuilder message = new StringBuilder($"Starting device on {path} with description: {Description}, frequency: {frequency} and {(sources.Count == 0 ? "no sources" : "sources:")}");
                foreach (PluxDotNet.Source source in sources) { message.Append($"\n\tport = {source.port}, freqDivisor = {source.freqDivisor}, nBits = {source.nBits}, chMask = {source.chMask}"); }
                logger.Info(message);
            }
            using (source = new CancellationTokenSource())
            {
                plux?.Start(manager.frequency, Sources);
                plux?.Loop();
                plux?.Stop();
            }
            logger.Info("Cleaning up");
            plux?.Dispose();
            plux = null;
            source = null;
            lastFrame = -1;
            lock (sources) { sources.Clear(); }
            logger.Info("Shutting down");
        }

        public void Stop()
        {
            logger.Info($"Stopping device on {path}");
            try { source?.Cancel(); }
            catch (ObjectDisposedException) { }
            try { plux?.Interrupt(null); }
            catch (PluxDotNet.Exception.InvalidOperation) { }
        }
    }
}
