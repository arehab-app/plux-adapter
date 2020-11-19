using System;
using System.Text;
using System.Threading.Tasks;
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

            public override bool OnRawFrame(int nSeq, int[] data)
            {
                device.OnRawFrame(nSeq, data);
                return false;
            }

            public override bool OnInterrupt(object param)
            {
                return true;
            }
        }

        public sealed class FrameReceivedEventArgs : EventArgs
        {
            public readonly int lastSeq;
            public readonly int nSeq;
            public readonly ReadOnlyCollection<int> data;

            public FrameReceivedEventArgs(int lastSeq, int nSeq, int[] data)
            {
                this.lastSeq = lastSeq;
                this.nSeq = nSeq;
                this.data = Array.AsReadOnly(data);
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public event EventHandler<FrameReceivedEventArgs> FrameReceived;

        private readonly Manager manager;
        private Plux plux;
        private int lastSeq;

        public readonly string path;

        public Device(string path, Manager manager)
        {
            this.path = path;
            this.manager = manager;
        }

        private void OnRawFrame(int nSeq, int[] data)
        {
            FrameReceived?.Invoke(this, new FrameReceivedEventArgs(lastSeq, nSeq, data));
            int missing = nSeq - lastSeq;
            if (missing > 1) { logger.Warn($"Device on {path} dropped {missing - 1} frames"); }
            lastSeq = nSeq;

        }

        public async Task Start()
        {
            using (plux = new Plux(path, this))
            {
                StringBuilder message = new StringBuilder($"Connected to device on {path} with properties:");
                Dictionary<string, object> properties = plux.GetProperties();
                foreach (KeyValuePair<string, object> kvp in properties) { message.Append($"\n\t{kvp.Key} = {kvp.Value}"); }
                logger.Info(message);
                List<PluxDotNet.Source> sources = new List<PluxDotNet.Source>();
                if (properties.ContainsKey("description"))
                {
                    string description = properties["description"].ToString();
                    switch (description)
                    {
                        case "biosignalsplux":
                            sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 3, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 4, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 5, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 6, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 7, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 8, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            break;
                        case "MuscleBAN BE Plux":
                            sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.nBits, chMask = 7 });
                            break;
                        case "OpenBANPlux":
                            sources.Add(new PluxDotNet.Source { port = 1, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 2, freqDivisor = 1, nBits = manager.nBits, chMask = 1 });
                            sources.Add(new PluxDotNet.Source { port = 11, freqDivisor = 1, nBits = manager.nBits, chMask = 7 });
                            break;
                        default:
                            logger.Warn($"Device on {path} has unknown description: {description}");
                            break;
                    }
                }
                else { logger.Warn($"Device on {path} has no description"); }
                message.Clear();
                message.Append($"Starting device on {path} with freq = {manager.freq} and sources:");
                foreach (PluxDotNet.Source source in sources)
                {
                    message.Append($"\n\tport = {source.port}, freqDivisor = {source.freqDivisor}, nBits = {source.nBits}, chMask = {source.chMask}");
                }
                logger.Info(message);
                plux.Start(manager.freq, sources);
                plux.Loop();
                plux.Stop();
            }
        }

        public void Stop()
        {
            logger.Info($"Interrupting loop of device on {path}");
            plux.Interrupt(null);
        }
    }
}
