using System.Collections.Generic;

using NLog;

namespace PluxAdapter
{
    public sealed class Manager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();

        public readonly float freq;
        public readonly int nBits;

        public Dictionary<string, Device> Devices { get { lock (devices) { return new Dictionary<string, Device>(devices); } } }

        public Manager(float freq, int nBits)
        {
            this.freq = freq;
            this.nBits = nBits;
        }

        private Device Connect(string path)
        {
            logger.Info($"Connecting to device on {path}");
            Device device = new Device(path, this);
            device.Start();
            devices[path] = device;
            return device;
        }

        public Dictionary<string, Device> Scan(string domain)
        {
            logger.Info($"Scanning for devices in {domain}");
            Dictionary<string, Device> found = new Dictionary<string, Device>();
            lock (devices)
            {
                foreach (PluxDotNet.DevInfo devInfo in PluxDotNet.SignalsDev.FindDevices(domain))
                {
                    if (devices.ContainsKey(devInfo.path)) { continue; }
                    found[devInfo.path] = Connect(devInfo.path);
                }
            }
            return found;
        }

        public Device Get(string path)
        {
            lock (devices)
            {
                if (devices.ContainsKey(path)) { return devices[path]; }
                return Connect(path);
            }
        }

        public void Stop()
        {
            lock (devices)
            {
                foreach (Device device in devices.Values)
                {
                    device.Stop();
                }
            }
        }
    }
}
