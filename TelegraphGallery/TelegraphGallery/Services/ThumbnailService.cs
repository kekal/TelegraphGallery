using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Serilog;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class ThumbnailService : IThumbnailService
    {
        private const int ThumbnailJpegQuality = 60;
        private const int CachePruneDays = 30;
        private const int CacheResolution = 400;

        private static readonly string CacheDir = AppPaths.ThumbnailCacheDir;

        public ThumbnailService()
        {
            Directory.CreateDirectory(CacheDir);
            PruneOldEntries();
        }

        public async Task<BitmapSource> GenerateThumbnailAsync(string filePath, CancellationToken ct = default)
        {
            var cacheKey = FileHashHelper.ComputeMetadataCacheKey(filePath);
            var cachePath = Path.Combine(CacheDir, cacheKey + ".jpg");

            // Fast path: load from our cache
            if (File.Exists(cachePath))
            {
                ct.ThrowIfCancellationRequested();
                return await Task.Run(() => LoadBitmapFromFile(cachePath), ct).ConfigureAwait(false);
            }

            // Windows Shell (STA thread) — leverages Explorer's thumbnail cache and codecs
            ct.ThrowIfCancellationRequested();
            var bitmap = await ShellThumbnailHelper.GetThumbnailAsync(filePath, CacheResolution).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await Task.Run(() => SaveToCache(bitmap, cachePath), ct).ConfigureAwait(false);
            return bitmap;
        }

        private static BitmapSource LoadBitmapFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var bitmap = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static void SaveToCache(BitmapSource bitmap, string cachePath)
        {
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = ThumbnailJpegQuality };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fs = File.Create(cachePath);
                encoder.Save(fs);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to cache thumbnail at {Path}", cachePath);
            }
        }

        public void ClearCache()
        {
            try
            {
                foreach (var file in Directory.GetFiles(CacheDir))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clear thumbnail cache");
            }
        }

        private static void PruneOldEntries()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-CachePruneDays);
                foreach (var file in Directory.GetFiles(CacheDir))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to prune old thumbnail cache entries");
            }
        }
    }
}
