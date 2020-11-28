using System;
using System.Threading.Tasks;
using System.Collections.Generic;

using NLog;

namespace PluxAdapter
{
    /// <summary>
    /// Manages <see cref="PluxAdapter.Device" /> connections. All public members are threadsafe.
    /// </summary>
    public sealed class Manager
    {
        /// <summary>
        /// <see cref="NLog.Logger" /> used by <see cref="PluxAdapter.Manager" />.
        /// </summary>
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Managed <see cref="PluxAdapter.Device" /> mapped to path.
        /// </summary>
        private readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();
        /// <summary>
        /// Parallel <see cref="System.Threading.Tasks.Task" /> used for managed <see cref="PluxAdapter.Device" />.
        /// </summary>
        private readonly List<Task> tasks = new List<Task>();

        /// <summary>
        /// Connection base frequency for connected <see cref="PluxAdapter.Device" />.
        /// </summary>
        public readonly float frequency;
        /// <summary>
        /// Data resolution for connected <see cref="PluxAdapter.Device" />.
        /// </summary>
        public readonly int resolution;

        /// <summary>
        /// Managed <see cref="PluxAdapter.Device" />. This is threadsafe.
        /// </summary>
        public Dictionary<string, Device> Devices { get { lock (devices) { return new Dictionary<string, Device>(devices); } } }

        /// <summary>
        /// Creates new <see cref="PluxAdapter.Manager" /> with <see cref="PluxAdapter.Device" /> connection base <paramref name="frequency" /> and data <paramref name="resolution" />.
        /// </summary>
        /// <param name="frequency">Base frequency for newly connected <see cref="PluxAdapter.Device" /> to use.</param>
        /// <param name="resolution">Data resolution for newly connected <see cref="PluxAdapter.Device" /> to use.</param>
        public Manager(float frequency, int resolution)
        {
            this.frequency = frequency;
            this.resolution = resolution;
        }

        /// <summary>
        /// Attempts to connect to <see cref="PluxAdapter.Device" /> on <paramref name="path" />.
        /// </summary>
        /// <param name="path">Path to look for <see cref="PluxAdapter.Device" /> on.</param>
        /// <returns>Already connected <see cref="PluxAdapter.Device" /> or <see langword="null" /> if nothing was found.</returns>
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

        /// <summary>
        /// Scans for <see cref="PluxAdapter.Device" /> in <paramref name="domain" />. This is threadsafe.
        /// </summary>
        /// <param name="domain">Domain to scan.</param>
        /// <returns>Newly found <see cref="PluxAdapter.Device" /> mapped to path.</returns>
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

        /// <summary>
        /// Gets connected <see cref="PluxAdapter.Device" /> on <paramref name="path" />. <see cref="PluxAdapter.Device" /> may be returned from cache if it was requested before. This is threadsafe.
        /// </summary>
        /// <param name="path">Path to look for <see cref="PluxAdapter.Device" /> on.</param>
        /// <returns>Already connected <see cref="PluxAdapter.Device" /> or <see langword="null" /> if nothing was found.</returns>
        public Device Get(string path)
        {
            lock (devices)
            {
                if (devices.ContainsKey(path)) { return devices[path]; }
                return Connect(path);
            }
        }

        /// <summary>
        /// Stops <see cref="PluxAdapter.Manager" /> and it's monitored <see cref="PluxAdapter.Manager.devices" /> and <see cref="PluxAdapter.Manager.tasks" />. This is threadsafe.
        /// </summary>
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
