using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PluxAdapter
{
    /// <summary>
    /// Extends <see cref="System.IO.Stream" /> with methods to fill <see cref="byte" /> buffers and detect end of <see cref="System.IO.Stream" />.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Creates and fills <see cref="byte" /> buffer of <paramref name="length" /> from <paramref name="stream" /> while monitoring <paramref name="cancellationToken" />.
        /// </summary>
        /// <param name="stream">Stream to read from.</param>
        /// <param name="length">Length of buffer to create and fill.</param>
        /// <param name="cancellationToken">Token to monitor.</param>
        /// <returns>Filled <see cref="byte" /> buffer.</returns>
        /// <exception cref="System.IO.EndOfStreamException">Thrown when end of <paramref name="stream" /> is detected.</exception>
        public static async Task<byte[]> ReadAllAsync(this Stream stream, int length, CancellationToken cancellationToken)
        {
            // simply allocate and fill buffer
            byte[] buffer = new byte[length];
            await stream.ReadAllAsync(buffer, cancellationToken);
            return buffer;
        }

        /// <summary>
        /// Fills <paramref name="buffer" /> from <paramref name="stream" /> while monitoring <paramref name="cancellationToken" />.
        /// </summary>
        /// <param name="stream">Stream to read from.</param>
        /// <param name="buffer">Buffer to fill.</param>
        /// <param name="cancellationToken">Token to monitor.</param>
        /// <returns>Length of <paramref name="buffer" />.</returns>
        /// <exception cref="System.IO.EndOfStreamException">Thrown when end of <paramref name="stream" /> is detected.</exception>
        public static async Task<int> ReadAllAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            // shortcut, also note that stream.ReadAsync seems to hang on buffers with 0 length
            if (buffer.Length == 0) { return 0; }
            int read = 0;
            int check = 0;
            // loop till buffer is full, note that 0 indicates end of stream
            while ((read += (check = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken))) < buffer.Length) { if (check == 0) { throw new EndOfStreamException(); } }
            return read;
        }
    }
}
