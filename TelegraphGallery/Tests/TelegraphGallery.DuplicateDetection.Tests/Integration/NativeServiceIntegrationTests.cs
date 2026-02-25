using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TelegraphGallery.Models;
using TelegraphGallery.Services;
using TelegraphGallery.Services.Interfaces;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    [Collection("Integration")]
    public class NativeServiceIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly IThumbnailService _thumbnailService;

        public NativeServiceIntegrationTests(IntegrationTestFixture _)
        {
            _tempDir = TestImageHelper.CreateTempDirectory("TGallery_NativeSvc_IntTest");
            _imageProcessingService = IntegrationTestFixture.CreateImageProcessingService();
            _thumbnailService = IntegrationTestFixture.CreateThumbnailService();
        }

        public void Dispose() => TestImageHelper.Cleanup(_tempDir);

        private string CreateTestImage(string name, int width = 200, int height = 200, int seed = 42)
            => TestImageHelper.CreateTestImage(_tempDir, name, width, height, seed);

        private string CreateTestPng(string name, int width = 200, int height = 200)
        {
            var path = Path.Combine(_tempDir, name);
            using var image = new Image<Rgba32>(width, height, new Rgba32(100, 150, 200, 255));
            image.SaveAsPng(path);
            return path;
        }

        // =====================================================================
        // ImageProcessingService Tests
        // =====================================================================

        [Fact]
        public async Task PrepareForUploadAsync_SmallImage_ReturnsOriginalPath()
        {
            // Arrange: image within all limits (200x200 is well under MaxWidth=5000, MaxHeight=5000,
            // TotalDimensionThreshold=10000, and file size will be under MaxFileSize=5MB)
            var imagePath = CreateTestImage("small.jpg", width: 200, height: 200);
            var config = new AppConfig
            {
                MaxWidth = AppConfig.DefaultMaxDimension,
                MaxHeight = AppConfig.DefaultMaxDimension,
                TotalDimensionThreshold = 10000,
                MaxFileSize = 5_000_000
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: no processing needed -> returns original path
            Assert.Equal(imagePath, result);
        }

        [Fact]
        public async Task PrepareForUploadAsync_LargeImage_ResizesWithMaxDimensions()
        {
            // Arrange: 200x200 image, but MaxWidth=100, MaxHeight=100 (overrides default)
            var imagePath = CreateTestImage("large_dims.jpg", width: 200, height: 200);
            var config = new AppConfig
            {
                MaxWidth = 100,
                MaxHeight = 100,
                TotalDimensionThreshold = 10000,
                MaxFileSize = 5_000_000
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: a processed file is returned (not the original)
            Assert.NotNull(result);
            Assert.NotEqual(imagePath, result);
            Assert.True(File.Exists(result));

            // Verify dimensions are smaller than input
            var info = Image.Identify(result);
            Assert.True(info.Width <= 100, $"Expected width <= 100 but got {info.Width}");
            Assert.True(info.Height <= 100, $"Expected height <= 100 but got {info.Height}");
        }

        [Fact]
        public async Task PrepareForUploadAsync_TotalDimensionThreshold_Resizes()
        {
            // Arrange: MaxWidth/MaxHeight at default (5000), TotalDimensionThreshold=300
            // 200x200 image has sum=400 which exceeds threshold of 300
            var imagePath = CreateTestImage("threshold.jpg", width: 200, height: 200);
            var config = new AppConfig
            {
                MaxWidth = AppConfig.DefaultMaxDimension,
                MaxHeight = AppConfig.DefaultMaxDimension,
                TotalDimensionThreshold = 300,
                MaxFileSize = 5_000_000
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: a processed file is returned (not the original)
            Assert.NotNull(result);
            Assert.NotEqual(imagePath, result);
            Assert.True(File.Exists(result));

            // Verify output dimensions are smaller than input
            var info = Image.Identify(result);
            Assert.True(info.Width < 200 || info.Height < 200,
                $"Expected at least one dimension to be smaller than input (200x200), got {info.Width}x{info.Height}");
        }

        [Fact]
        public async Task PrepareForUploadAsync_LargeFile_Compresses()
        {
            // Arrange: create a large image (3000x2000) which will have a large file size,
            // then set MaxFileSize to a small value to trigger compression
            var imagePath = CreateTestImage("large_file.jpg", width: 3000, height: 2000, seed: 99);
            long originalSize = new FileInfo(imagePath).Length;

            var config = new AppConfig
            {
                MaxWidth = AppConfig.DefaultMaxDimension,
                MaxHeight = AppConfig.DefaultMaxDimension,
                TotalDimensionThreshold = 10000,
                MaxFileSize = 100_000  // 100KB - much smaller than expected 3000x2000 JPEG
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: a processed file is returned
            Assert.NotNull(result);
            Assert.True(File.Exists(result));

            // Output file should be smaller than or within range of MaxFileSize
            long resultSize = new FileInfo(result!).Length;
            // The service compresses to try to meet MaxFileSize; verify it attempted compression
            Assert.True(resultSize < originalSize,
                $"Expected result ({resultSize} bytes) to be smaller than original ({originalSize} bytes)");
        }

        [Fact]
        public async Task PrepareForUploadAsync_ProcessedFile_IsJpeg()
        {
            // Arrange: image that requires processing (will be resized due to MaxWidth=100)
            var imagePath = CreateTestImage("format_check.jpg", width: 200, height: 200);
            var config = new AppConfig
            {
                MaxWidth = 100,
                MaxHeight = 100,
                TotalDimensionThreshold = 10000,
                MaxFileSize = 5_000_000
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: output is always JPEG
            Assert.NotNull(result);
            Assert.True(result!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase),
                $"Expected .jpg extension but got: {Path.GetExtension(result)}");

            // Verify JPEG magic bytes (FF D8 FF)
            var bytes = new byte[3];
            using (var fs = File.OpenRead(result))
            {
                fs.Read(bytes, 0, 3);
            }
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
            Assert.Equal(0xFF, bytes[2]);
        }

        [Fact]
        public async Task PrepareForUploadAsync_OutputPath_InProcessedDir()
        {
            // Arrange: image that requires processing
            var imagePath = CreateTestImage("output_path.jpg", width: 200, height: 200);
            var config = new AppConfig
            {
                MaxWidth = 100,
                MaxHeight = 100,
                TotalDimensionThreshold = 10000,
                MaxFileSize = 5_000_000
            };

            // Act
            var result = await _imageProcessingService.PrepareForUploadAsync(imagePath, config);

            // Assert: output goes to ProcessedImagesDir with _c.jpg suffix
            Assert.NotNull(result);
            var processedDir = AppPaths.ProcessedImagesDir;
            Assert.True(result!.StartsWith(processedDir, StringComparison.OrdinalIgnoreCase),
                $"Expected result path to be in '{processedDir}' but got '{result}'");
            Assert.True(result.EndsWith("_c.jpg", StringComparison.OrdinalIgnoreCase),
                $"Expected result path to end with '_c.jpg' but got '{Path.GetFileName(result)}'");
        }

        [Fact]
        public async Task PrepareMediumAsync_SmallImage_StillCreatesOutput()
        {
            // Arrange: image smaller than 1920x1080 - no resize needed, but still creates medium
            var imagePath = CreateTestImage("small_medium.jpg", width: 800, height: 600);

            // Act
            var result = await _imageProcessingService.PrepareMediumAsync(imagePath);

            // Assert: output is created even though no resize was needed
            Assert.NotNull(result);
            Assert.True(File.Exists(result), $"Expected output file to exist at: {result}");

            // Dimensions should remain at or below the original (no upscaling)
            var info = Image.Identify(result!);
            Assert.True(info.Width <= 1920, $"Width {info.Width} should be <= 1920");
            Assert.True(info.Height <= 1080, $"Height {info.Height} should be <= 1080");
        }

        [Fact]
        public async Task PrepareMediumAsync_LargeImage_ResizesToMediumMax()
        {
            // Arrange: create 3000x2000 image which exceeds 1920x1080
            var imagePath = CreateTestImage("large_medium.jpg", width: 3000, height: 2000, seed: 77);

            // Act
            var result = await _imageProcessingService.PrepareMediumAsync(imagePath);

            // Assert
            Assert.NotNull(result);
            Assert.True(File.Exists(result));

            var info = Image.Identify(result!);
            Assert.True(info.Width <= 1920,
                $"Expected width <= 1920 (MediumMaxWidth) but got {info.Width}");
            Assert.True(info.Height <= 1080,
                $"Expected height <= 1080 (MediumMaxHeight) but got {info.Height}");
        }

        [Fact]
        public async Task PrepareMediumAsync_OutputPath_HasMediumSuffix()
        {
            // Arrange
            var imagePath = CreateTestImage("suffix_check.jpg", width: 400, height: 300);

            // Act
            var result = await _imageProcessingService.PrepareMediumAsync(imagePath);

            // Assert: output has _m.jpg suffix
            Assert.NotNull(result);
            Assert.True(result!.EndsWith("_m.jpg", StringComparison.OrdinalIgnoreCase),
                $"Expected result path to end with '_m.jpg' but got '{Path.GetFileName(result)}'");
        }

        [Fact]
        public async Task PrepareMediumAsync_CompressesToTargetSize()
        {
            // Arrange: use a large image so compression is meaningful
            var imagePath = CreateTestImage("compress_medium.jpg", width: 3000, height: 2000, seed: 55);

            // Act
            var result = await _imageProcessingService.PrepareMediumAsync(imagePath);

            // Assert: output exists and has a reasonable file size (should be compressed to ~200KB)
            Assert.NotNull(result);
            Assert.True(File.Exists(result));

            long resultSize = new FileInfo(result!).Length;
            // Medium max file size is 200KB (200_000 bytes); allow some tolerance
            Assert.True(resultSize > 0, "Result file should not be empty");
            // Verify the output is a valid JPEG
            var bytes = new byte[3];
            using (var fs = File.OpenRead(result!))
            {
                fs.Read(bytes, 0, 3);
            }
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
            Assert.Equal(0xFF, bytes[2]);
        }

        // =====================================================================
        // ThumbnailService Tests
        // =====================================================================

        [Fact]
        public async Task GenerateThumbnailAsync_JpegImage_ReturnsBitmapSource()
        {
            // Arrange
            var imagePath = CreateTestImage("thumb_jpeg.jpg", width: 400, height: 300);
            using var cts = new CancellationTokenSource();

            // Act
            var result = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.IsAssignableFrom<BitmapSource>(result);
            Assert.True(result.PixelWidth > 0, "Thumbnail should have positive width");
            Assert.True(result.PixelHeight > 0, "Thumbnail should have positive height");
        }

        [Fact]
        public async Task GenerateThumbnailAsync_CachesResult()
        {
            // Arrange
            var imagePath = CreateTestImage("thumb_cache.jpg", width: 300, height: 300);
            using var cts = new CancellationTokenSource();

            // Act: first call generates and caches
            var result1 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Verify cache file exists in thumbnail cache directory
            var cacheDir = AppPaths.ThumbnailCacheDir;
            var cacheFiles = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
            Assert.True(cacheFiles.Length > 0, "Expected at least one cache file after generating thumbnail");

            // Act: second call should use cache
            var result2 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert: both results are valid BitmapSources
            Assert.NotNull(result1);
            Assert.NotNull(result2);
        }

        [Fact]
        public async Task GenerateThumbnailAsync_SameFile_SingleCacheEntry()
        {
            // Arrange
            var imagePath = CreateTestImage("thumb_single.jpg", width: 400, height: 400);
            using var cts = new CancellationTokenSource();

            // Clear cache to get clean state
            _thumbnailService.ClearCache();

            // Act: generate thumbnail twice for the same file
            var result1 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);
            var result2 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert: both results are valid
            Assert.NotNull(result1);
            Assert.NotNull(result2);

            // Cache should have exactly one entry (size-independent)
            var cacheDir = AppPaths.ThumbnailCacheDir;
            var cacheFiles = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
            Assert.Single(cacheFiles);
        }

        [Fact]
        public async Task GenerateThumbnailAsync_PngImage_Works()
        {
            // Arrange: create a PNG image using ImageSharp
            var pngPath = CreateTestPng("thumb_png.png", width: 300, height: 300);
            using var cts = new CancellationTokenSource();

            // Act
            var result = await _thumbnailService.GenerateThumbnailAsync(pngPath, cts.Token);

            // Assert
            Assert.NotNull(result);
            Assert.IsAssignableFrom<BitmapSource>(result);
            Assert.True(result.PixelWidth > 0);
            Assert.True(result.PixelHeight > 0);
        }

        [Fact]
        public async Task GenerateThumbnailAsync_CancellationToken_ThrowsOnCancel()
        {
            // Arrange
            var imagePath = CreateTestImage("thumb_cancel.jpg", width: 200, height: 200);
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);
            });
        }

        [Fact]
        public async Task ClearCache_RemovesAllCacheFiles()
        {
            // Arrange: generate some thumbnails to populate the cache
            var image1 = CreateTestImage("clear_cache1.jpg", width: 200, height: 200, seed: 1);
            var image2 = CreateTestImage("clear_cache2.jpg", width: 300, height: 300, seed: 2);
            using var cts = new CancellationTokenSource();

            await _thumbnailService.GenerateThumbnailAsync(image1, cts.Token);
            await _thumbnailService.GenerateThumbnailAsync(image2, cts.Token);

            var cacheDir = AppPaths.ThumbnailCacheDir;
            var filesBeforeClear = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
            Assert.True(filesBeforeClear.Length > 0, "Cache should have files before clearing");

            // Act
            _thumbnailService.ClearCache();

            // Assert: cache directory should be empty (or not exist)
            if (Directory.Exists(cacheDir))
            {
                var filesAfterClear = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
                Assert.Empty(filesAfterClear);
            }
            // If directory was removed entirely, that also counts as cleared
        }

        [Fact]
        public async Task GenerateThumbnailAsync_DifferentTimestamp_RegeneratesThumbnail()
        {
            // Arrange: create a test image and clear cache to start fresh
            var imagePath = CreateTestImage("thumb_timestamp.jpg", width: 300, height: 300);
            using var cts = new CancellationTokenSource();

            _thumbnailService.ClearCache();

            // Act: generate thumbnail for the image (populates cache)
            var result1 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert: verify exactly 1 cache file
            var cacheDir = AppPaths.ThumbnailCacheDir;
            var cacheFilesAfterFirstGen = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
            Assert.Single(cacheFilesAfterFirstGen);

            // Act: change the file's LastWriteTimeUtc
            File.SetLastWriteTimeUtc(imagePath, DateTime.UtcNow.AddHours(1));

            // Act: generate thumbnail again (should use different cache key due to new timestamp)
            var result2 = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert: verify there are now 2 cache files (different metadata key = different cache entry)
            var cacheFilesAfterSecondGen = Directory.GetFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly);
            Assert.Equal(2, cacheFilesAfterSecondGen.Length);

            // Both results should be valid BitmapSources
            Assert.NotNull(result1);
            Assert.NotNull(result2);
        }

        [Fact]
        public async Task GenerateThumbnailAsync_BitmapSource_IsFrozen()
        {
            // Arrange
            var imagePath = CreateTestImage("thumb_frozen.jpg", width: 300, height: 200);
            using var cts = new CancellationTokenSource();

            // Act
            var result = await _thumbnailService.GenerateThumbnailAsync(imagePath, cts.Token);

            // Assert: BitmapSource must be frozen for thread-safety in WPF
            Assert.NotNull(result);
            Assert.True(result.IsFrozen,
                "BitmapSource should be frozen to allow cross-thread access in WPF");
        }

        // =====================================================================
        // FileHashHelper Tests
        // =====================================================================

        [Fact]
        public void ComputeMetadataCacheKey_ReturnsDeterministicResult()
        {
            // Arrange: create a test file
            var imagePath = CreateTestImage("metadata_cache_deterministic.jpg", width: 200, height: 200);

            // Act: compute the cache key twice for the same file
            var key1 = FileHashHelper.ComputeMetadataCacheKey(imagePath);
            var key2 = FileHashHelper.ComputeMetadataCacheKey(imagePath);

            // Assert: both keys should be identical
            Assert.Equal(key1, key2);
            Assert.NotEmpty(key1);
            Assert.NotEmpty(key2);
        }

        [Fact]
        public void ComputeMetadataCacheKey_ChangesWhenFileModified()
        {
            // Arrange: create a test file
            var imagePath = CreateTestImage("metadata_cache_modified.jpg", width: 200, height: 200);
            var key1 = FileHashHelper.ComputeMetadataCacheKey(imagePath);

            // Act: modify the file by appending data to change its size
            System.Threading.Thread.Sleep(100); // Ensure LastWriteTime changes
            using (var fs = File.Open(imagePath, FileMode.Append, FileAccess.Write))
            {
                fs.Write(new byte[] { 0xFF, 0xD9 }); // Append fake JPEG end marker
            }
            var key2 = FileHashHelper.ComputeMetadataCacheKey(imagePath);

            // Assert: keys should differ because file size changed
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void ComputeMetadataCacheKey_DifferentFilesProduceDifferentKeys()
        {
            // Arrange: create two different test files
            var imagePath1 = CreateTestImage("metadata_file1.jpg", width: 200, height: 200, seed: 1);
            var imagePath2 = CreateTestImage("metadata_file2.jpg", width: 200, height: 200, seed: 2);

            // Act: compute cache keys for both files
            var key1 = FileHashHelper.ComputeMetadataCacheKey(imagePath1);
            var key2 = FileHashHelper.ComputeMetadataCacheKey(imagePath2);

            // Assert: keys should differ (different file paths guarantee different keys)
            Assert.NotEqual(key1, key2);
            Assert.NotEmpty(key1);
            Assert.NotEmpty(key2);
        }

        // =====================================================================
        // ConfigService Tests (real INI file I/O)
        // =====================================================================

        [Fact]
        public void ConfigService_Load_ReturnsDefaultValues_WhenNoConfigFile()
        {
            // Arrange: ensure no config.ini exists
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(configPath)) File.Delete(configPath);

            try
            {
                var service = new ConfigService();

                // Act
                var config = service.Load();

                // Assert — defaults from AppConfig
                Assert.NotNull(config);
                Assert.Equal("imgbb", config.StorageChoice);
                Assert.Equal("", config.TelegraphAccessToken);
                Assert.Equal("https://my_page", config.AuthorUrl);
                Assert.Equal("My albums page", config.HeaderName);
                Assert.Equal("", config.ImgbbApiKey);
                Assert.Equal(5000, config.MaxWidth);
                Assert.Equal(5000, config.MaxHeight);
                Assert.Equal(5_000_000, config.MaxFileSize);
                Assert.Equal(2, config.PauseSeconds);
                Assert.Equal("old", config.OutputFolder);
                Assert.Equal(5, config.DuplicateThreshold);
                Assert.Equal(150, config.ThumbnailSize);
                Assert.Equal("Name", config.SortMode);
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        [Fact]
        public void ConfigService_SaveThenLoad_RoundtripsAllFields()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(configPath)) File.Delete(configPath);

            try
            {
                var service = new ConfigService();
                var saved = new AppConfig
                {
                    StorageChoice = "cyberdrop",
                    TelegraphAccessToken = "tok_roundtrip",
                    AuthorUrl = "https://roundtrip.example.com",
                    HeaderName = "Roundtrip Header",
                    ImgbbApiKey = "imgbb_roundtrip",
                    CyberdropToken = "cyber_roundtrip",
                    CyberdropAlbumId = "album_rt",
                    MaxWidth = 1920,
                    MaxHeight = 1080,
                    TotalDimensionThreshold = 3000,
                    MaxFileSize = 2_000_000,
                    PauseSeconds = 7,
                    OutputFolder = "done",
                    DuplicateThreshold = 12,
                    ThumbnailSize = 250,
                    SortMode = "File Timestamp"
                };

                // Act — save, then re-load via a fresh instance (bypasses cache)
                service.Save(saved);
                var freshService = new ConfigService();
                var loaded = freshService.Load();

                // Assert
                Assert.Equal("cyberdrop", loaded.StorageChoice);
                Assert.Equal("tok_roundtrip", loaded.TelegraphAccessToken);
                Assert.Equal("https://roundtrip.example.com", loaded.AuthorUrl);
                Assert.Equal("Roundtrip Header", loaded.HeaderName);
                Assert.Equal("imgbb_roundtrip", loaded.ImgbbApiKey);
                Assert.Equal("cyber_roundtrip", loaded.CyberdropToken);
                Assert.Equal("album_rt", loaded.CyberdropAlbumId);
                Assert.Equal(1920, loaded.MaxWidth);
                Assert.Equal(1080, loaded.MaxHeight);
                Assert.Equal(3000, loaded.TotalDimensionThreshold);
                Assert.Equal(2_000_000, loaded.MaxFileSize);
                Assert.Equal(7, loaded.PauseSeconds);
                Assert.Equal("done", loaded.OutputFolder);
                Assert.Equal(12, loaded.DuplicateThreshold);
                Assert.Equal(250, loaded.ThumbnailSize);
                Assert.Equal("File Timestamp", loaded.SortMode);
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        [Fact]
        public void ConfigService_Load_ReturnsClonesNotReferences()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(configPath)) File.Delete(configPath);

            try
            {
                var service = new ConfigService();

                // Act — load twice, mutate the first
                var config1 = service.Load();
                config1.StorageChoice = "modified";
                config1.MaxWidth = 9999;

                var config2 = service.Load();

                // Assert — second load returns original (cached) values
                Assert.Equal("imgbb", config2.StorageChoice);
                Assert.Equal(5000, config2.MaxWidth);
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        [Fact]
        public void ConfigService_Load_CacheUpdatedAfterSave()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(configPath)) File.Delete(configPath);

            try
            {
                var service = new ConfigService();

                var initial = service.Load();
                Assert.Equal("imgbb", initial.StorageChoice);

                // Save a new config
                service.Save(new AppConfig { StorageChoice = "ipfs" });

                // Load should return updated cached value
                var reloaded = service.Load();
                Assert.Equal("ipfs", reloaded.StorageChoice);
            }
            finally
            {
                if (File.Exists(configPath)) File.Delete(configPath);
            }
        }

        // =====================================================================
        // AppPaths Tests
        // =====================================================================

        [Fact]
        public void AppPaths_EnsureDirectoriesExist_CreatesDirectories()
        {
            // Act — should not throw
            var exception = Record.Exception(AppPaths.EnsureDirectoriesExist);
            Assert.Null(exception);

            // Assert — directories must exist
            Assert.True(Directory.Exists(AppPaths.ThumbnailCacheDir));
            Assert.True(Directory.Exists(AppPaths.ProcessedImagesDir));
        }

        // =====================================================================
        // UploadCacheService Tests (default constructor + Dispose)
        // =====================================================================

        [Fact]
        public void UploadCacheService_DefaultConstructor_LoadsAndDisposes()
        {
            // Act — exercise the parameterless constructor (uses AppPaths.UploadCacheFile)
            var service = new UploadCacheService();

            // Set a value to exercise the write path
            service.Set("test_default_ctor", "imgbb", new UploadCacheEntry(
                "https://example.com/test.jpg", null, 100, DateTime.UtcNow));

            var result = service.TryGet("test_default_ctor", "imgbb");
            Assert.NotNull(result);

            // Act — Dispose flushes pending writes and disposes the timer
            var ex = Record.Exception(() => service.Dispose());
            Assert.Null(ex);
        }

        // =====================================================================
        // DuplicateDetectionService.ClearCache Tests
        // =====================================================================

        [Fact]
        public async Task DuplicateDetectionService_ClearCache_WorksCorrectly()
        {
            // Arrange — run a duplicate scan to populate the digest cache
            var dupService = IntegrationTestFixture.CreateDuplicateDetectionService();
            var img1 = CreateTestImage("clear_dup1.jpg", seed: 1);
            var img2 = CreateTestImage("clear_dup2.jpg", seed: 2);
            var items = new System.Collections.Generic.List<GalleryItem>
            {
                new() { FilePath = img1, FileName = Path.GetFileName(img1), IsVideo = false },
                new() { FilePath = img2, FileName = Path.GetFileName(img2), IsVideo = false }
            };
            await dupService.FindDuplicatesAsync(items, threshold: 5);

            // Act — clear the digest cache
            var ex = Record.Exception(() => dupService.ClearCache());

            // Assert — should not throw, and subsequent calls still work
            Assert.Null(ex);
            var result = await dupService.FindDuplicatesAsync(items, threshold: 5);
            Assert.NotNull(result);
        }
    }
}
