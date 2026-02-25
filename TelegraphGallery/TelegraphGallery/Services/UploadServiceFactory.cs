using DryIoc;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class UploadServiceFactory(IContainer container) : IUploadServiceFactory
    {
        public IUploadService Create(string storageChoice)
        {
            return container.Resolve<IUploadService>(serviceKey: storageChoice.ToLowerInvariant());
        }
    }
}
