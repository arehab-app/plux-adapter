using Logger = NLog.Logger;
using LogManager = NLog.LogManager;

namespace PluxProxy
{
    public class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static void Main(string[] args)
        {
            logger.Debug("TODO");
            LogManager.Shutdown();
        }
    }
}
