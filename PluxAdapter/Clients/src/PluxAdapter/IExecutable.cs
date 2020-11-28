using System.Threading.Tasks;

namespace PluxAdapter
{
    /// <summary>
    /// Executable command.
    /// </summary>
    public interface IExecutable
    {
        /// <summary>
        /// Runs <see cref="PluxAdapter.IExecutable" /> loop.
        /// </summary>
        /// <returns><see cref="int" /> indicating <see cref="PluxAdapter.IExecutable" /> loop exit reason.</returns>
        Task<int> Start();
        /// <summary>
        /// Stops <see cref="PluxAdapter.IExecutable" />. This is threadsafe.
        /// </summary>
        void Stop();
    }
}
