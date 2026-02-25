using System.Threading;
using System.Threading.Tasks;
using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IUploadService
    {
        Task<UploadResult> UploadFileAsync(string filePath, AppConfig config, CancellationToken ct);
    }
}
