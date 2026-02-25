using System.Threading;
using System.Threading.Tasks;
using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IUploadCacheService
    {
        Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default);
        UploadCacheEntry? TryGet(string contentHash, string storageProvider);
        void Set(string contentHash, string storageProvider, UploadCacheEntry entry);
        void Flush();
        void ClearAll();
    }
}
