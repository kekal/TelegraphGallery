using System.Collections.Generic;
using System.Threading.Tasks;
using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface ITelegraphService
    {
        Task<string> CreatePageAsync(string title, List<UploadResult> results,
            AppConfig config);
    }
}
