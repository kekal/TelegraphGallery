using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IConfigService
    {
        AppConfig Load();
        void Save(AppConfig config);
    }
}
