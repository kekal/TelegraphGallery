using System.IO;

namespace TelegraphGallery.Services
{
    internal static class AppPaths
    {
        private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "TelegraphGallery");

        public static string ThumbnailCacheDir => Path.Combine(BaseDir, "thumbs");
        public static string ProcessedImagesDir => Path.Combine(BaseDir, "processed");
        public static string UploadCacheFile => Path.Combine(BaseDir, "upload_cache.json");

        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(ThumbnailCacheDir);
            Directory.CreateDirectory(ProcessedImagesDir);
            Directory.CreateDirectory(Path.GetDirectoryName(UploadCacheFile)!);
        }
    }
}
