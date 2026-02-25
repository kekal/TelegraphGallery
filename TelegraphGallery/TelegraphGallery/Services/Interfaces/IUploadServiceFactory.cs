namespace TelegraphGallery.Services.Interfaces
{
    public interface IUploadServiceFactory
    {
        IUploadService Create(string storageChoice);
    }
}
