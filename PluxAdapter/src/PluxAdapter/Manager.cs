using System;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;

namespace PluxAdapter
{
    public sealed class Manager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();
        private readonly List<Task> tasks = new List<Task>();

        public readonly float frequency;
        public readonly int resolution;

        public Dictionary<string, Device> Devices { get { lock (devices) { return new Dictionary<string, Device>(devices); } } }

        public Manager(float frequency, int resolution)
        {
            this.frequency = frequency;
            this.resolution = resolution;
        }

        private Device Connect(string path)
        {
            logger.Info($"Connecting to device on {path}");
            Device device = new Device(this, path);
            try { device.Connect(); }
            catch (PluxDotNet.Exception.DeviceNotFound)
            {
                logger.Warn("Device not found");
                device.Stop();
                return null;
            }
            catch (Exception) { device.Stop(); throw; }
            tasks.Add(Task.Run(() =>
            {
                try { device.Start(); }
                catch (Exception exc) { logger.Error(exc, "Something went wrong"); }
                finally { device.Stop(); }
            }));
            devices[path] = device;
            return device;
        }

        public Dictionary<string, Device> Scan(string domain)
        {
            logger.Info($"Scanning for new devices in {(domain.Length == 0 ? "all domains" : domain)}");
            Dictionary<string, Device> found = new Dictionary<string, Device>();
            lock (devices)
            {
                foreach (PluxDotNet.DevInfo devInfo in PluxDotNet.SignalsDev.FindDevices(domain))
                {
                    if (devices.ContainsKey(devInfo.path)) { continue; }
                    logger.Info($"Found new device on {devInfo.path} with description: {devInfo.description}");
                    Device device = Connect(devInfo.path);
                    if (!(device is null)) { found[devInfo.path] = device; }
                }
            }
            if (found.Count == 0) { logger.Info("No new devices found"); }
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
            logger.Info("Stopping");
            lock (devices)
            {
                foreach (Device device in devices.Values) { device.Stop(); }
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();
                devices.Clear();
            }
        }
    }
}
