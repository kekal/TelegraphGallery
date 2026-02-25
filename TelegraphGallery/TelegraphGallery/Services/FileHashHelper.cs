using System;
using System.IO;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelegraphGallery.Services
{
    internal static class FileHashHelper
    {
        private const int BufferSize = 81920;

        public static string ComputeMetadataCacheKey(string filePath)
        {
            var info = new FileInfo(filePath);
            var input = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var hash = XxHash128.Hash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static async Task<string> ComputeXxHash128Async(string filePath, CancellationToken ct = default)
        {
            var hash = new XxHash128();
            await using var stream = File.OpenRead(filePath);
            var buffer = new byte[BufferSize];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                hash.Append(buffer.AsSpan(0, bytesRead));
            }
            return Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant();
        }
    }
}
