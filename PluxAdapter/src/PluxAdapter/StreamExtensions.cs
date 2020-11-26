using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PluxAdapter
{
    public static class StreamExtensions
    {
        public static async Task<byte[]> ReadAllAsync(this Stream stream, int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            await stream.ReadAllAsync(buffer, cancellationToken);
            return buffer;
        }

        public static async Task<int> ReadAllAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0) { return 0; }
            int read = 0;
            int check = 0;
            while ((read += (check = await stream.ReadAsync(buffer, read, buffer.Length - read, cancellationToken))) < buffer.Length) { if (check == 0) { throw new EndOfStreamException(); } }
            return read;
        }
    }
}
