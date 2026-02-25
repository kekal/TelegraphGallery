using System.Threading.Tasks;
using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IImageProcessingService
    {
        Task<string?> PrepareForUploadAsync(string filePath, AppConfig config);
        Task<string?> PrepareMediumAsync(string filePath);
    }
}
