using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class ImageProcessingService : IImageProcessingService
    {
        private const int MediumMaxWidth = 1920;
        private const int MediumMaxHeight = 1080;
        private const long MediumMaxFileSize = 200_000; // 200KB
        private const int MediumInitialJpegQuality = 90;
        private const int MediumQualityStep = 5;
        private const int MediumMinJpegQuality = 5;
        private const int UploadInitialJpegQuality = 100;
        private const int UploadQualityStep = 1;
        private const int UploadMinJpegQuality = 1;

        /// <summary>
        /// Compresses an image by iterating from <paramref name="startQuality"/> down to
        /// <paramref name="minQuality"/> in steps of <paramref name="qualityStep"/>.
        /// Returns the compressed bytes at the first quality level that produces output
        /// at or below <paramref name="maxSize"/>. If even the minimum quality exceeds the
        /// target, the minimum-quality result is returned and the caller decides whether
        /// that is acceptable.
        /// </summary>
        private static byte[] CompressToTargetSize(
            Image image, long maxSize, int startQuality, int minQuality, int qualityStep)
        {
            byte[] result = null!;

            for (var quality = startQuality; quality >= minQuality; quality -= qualityStep)
            {
                var encoder = new JpegEncoder { Quality = quality };
                using var ms = new MemoryStream();
                image.Save(ms, encoder);
                result = ms.ToArray();

                if (result.Length <= maxSize)
                    break;
            }

            return result;
        }

        public async Task<string?> PrepareMediumAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var image = ImageLoadHelper.Load(filePath);

                    var width = image.Width;
                    var height = image.Height;

                    if (width > MediumMaxWidth || height > MediumMaxHeight)
                    {
                        var factor = Math.Min(
                            (double)MediumMaxHeight / height,
                            (double)MediumMaxWidth / width);
                        var newWidth = (int)(width * factor);
                        var newHeight = (int)(height * factor);
                        image.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                    }

                    var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                    var tempDir = AppPaths.ProcessedImagesDir;
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, nameWithoutExt + "_m.jpg");

                    var data = CompressToTargetSize(
                        image, MediumMaxFileSize,
                        MediumInitialJpegQuality, MediumMinJpegQuality, MediumQualityStep);

                    File.WriteAllBytes(tempPath, data);
                    return tempPath;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create medium version of {FilePath}", filePath);
                    return null;
                }
            }).ConfigureAwait(false);
        }

        public async Task<string?> PrepareForUploadAsync(string filePath, AppConfig config)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    using var image = ImageLoadHelper.Load(filePath);

                    var width = image.Width;
                    var height = image.Height;
                    var needsResize = false;
                    int newWidth = width, newHeight = height;

                    // Dimension validation
                    var dimensionsOverridden = config.MaxWidth != AppConfig.DefaultMaxDimension || config.MaxHeight != AppConfig.DefaultMaxDimension;

                    if (dimensionsOverridden)
                    {
                        if (width > config.MaxWidth || height > config.MaxHeight)
                        {
                            var factor = Math.Min(
                                (double)config.MaxHeight / height,
                                (double)config.MaxWidth / width);
                            newWidth = (int)(width * factor);
                            newHeight = (int)(height * factor);
                            needsResize = true;
                        }
                    }
                    else
                    {
                        if (width + height >= config.TotalDimensionThreshold)
                        {
                            newWidth = (int)((double)width * config.TotalDimensionThreshold / (width + height));
                            newHeight = (int)((double)height * config.TotalDimensionThreshold / (width + height));
                            needsResize = true;
                        }
                    }

                    var needsCompression = fileInfo.Length >= config.MaxFileSize;
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    var needsConversion = ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp");

                    if (!needsResize && !needsCompression && !needsConversion)
                    {
                        return filePath; // No processing needed
                    }

                    if (needsResize)
                    {
                        image.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                    }

                    var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                    var tempDir = AppPaths.ProcessedImagesDir;
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, nameWithoutExt + "_c.jpg");

                    if (needsCompression || needsResize)
                    {
                        var data = CompressToTargetSize(
                            image, config.MaxFileSize,
                            UploadInitialJpegQuality, UploadMinJpegQuality, UploadQualityStep);

                        if (data.Length <= config.MaxFileSize)
                        {
                            File.WriteAllBytes(tempPath, data);
                        }
                        else
                        {
                            Log.Error("Cannot compress {FilePath} below {MaxSize} bytes", filePath, config.MaxFileSize);
                            return null;
                        }
                    }
                    else
                    {
                        image.SaveAsJpeg(tempPath);
                    }

                    return tempPath;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to process image {FilePath}", filePath);
                    return null;
                }
            }).ConfigureAwait(false);
        }
    }
}
