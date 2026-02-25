using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IThumbnailService
    {
        Task<BitmapSource> GenerateThumbnailAsync(string filePath, CancellationToken ct = default);
        void ClearCache();
    }
}
