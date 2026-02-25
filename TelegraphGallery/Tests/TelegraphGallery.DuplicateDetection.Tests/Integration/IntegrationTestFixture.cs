using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Prism.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using TelegraphGallery.Models;
using TelegraphGallery.Services;
using TelegraphGallery.Services.Interfaces;
using TelegraphGallery.Services.Upload;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    /// <summary>
    /// xUnit collection definition that guarantees all integration tests run sequentially.
    /// All test classes marked with [Collection("Integration")] share the same fixture
    /// and will never execute in parallel.
    /// </summary>
    [CollectionDefinition("Integration")]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;

    /// <summary>
    /// Shared fixture for all integration tests.
    /// Sets up a WPF Application on an STA thread (required for Dispatcher, BitmapSource, etc.)
    /// and provides factory methods for creating real service instances.
    /// </summary>
    public class IntegrationTestFixture : IDisposable
    {
        private static int _initialized;
        private static Thread? _staThread;

        public IntegrationTestFixture()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
            {
                EnsureWpfApplication();
            }
        }

        /// <summary>
        /// Starts a WPF Application on a background STA thread so that
        /// Application.Current and Dispatcher are available in all tests.
        /// </summary>
        private static void EnsureWpfApplication()
        {
            if (Application.Current != null) return;

            var ready = new ManualResetEventSlim(false);
            _staThread = new Thread(() =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.Set();
                Dispatcher.Run();
            });
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Start();
            ready.Wait(TimeSpan.FromSeconds(10));
            // Give the dispatcher message loop time to start
            Thread.Sleep(200);
        }

        public void Dispose()
        {
            // Shared across all tests in the collection; do not shut down the app.
        }

        // ── Factory methods for real services ──────────────────────────────

        public static IEventAggregator CreateEventAggregator() => new EventAggregator();

        public static IConfigService CreateConfigService(string configDir)
        {
            // ConfigService uses a static path based on AppDomain.BaseDirectory.
            // For tests, we use a TestConfigService wrapper that points to a temp dir.
            return new TestConfigService(configDir);
        }

        public static IDuplicateDetectionService CreateDuplicateDetectionService() =>
            new DuplicateDetectionService();

        public static IThumbnailService CreateThumbnailService() =>
            new ThumbnailService();

        public static IImageProcessingService CreateImageProcessingService() =>
            new ImageProcessingService();

        public static ITelegraphService CreateTelegraphService() =>
            new TelegraphService();

        public static IUploadCacheService CreateUploadCacheService(string cacheFilePath) =>
            new UploadCacheService(cacheFilePath);

        public static IUploadServiceFactory CreateUploadServiceFactory() =>
            new TestUploadServiceFactory();

        public static TestProcessLauncher CreateProcessLauncher() =>
            new TestProcessLauncher();

        /// <summary>
        /// Returns the imgbb API key from IMGBB_API_KEY env var (set via test.runsettings).
        /// Calls Skip.If when not available.
        /// </summary>
        public static string RequireApiKey()
        {
            var envKey = Environment.GetEnvironmentVariable("IMGBB_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
                return envKey;

            var configService = new ConfigService();
            var key = configService.Load().ImgbbApiKey;
            Skip.If(string.IsNullOrEmpty(key),
                "IMGBB_API_KEY env var not set and config.ini has no key.");
            return key;
        }
    }

    // ── Test helpers ────────────────────────────────────────────────────

    /// <summary>
    /// IConfigService that reads/writes to a temp directory instead of the app's base directory.
    /// Backed by a real AppConfig instance.
    /// </summary>
    public class TestConfigService : IConfigService
    {
        private readonly string _configDir;
        private AppConfig? _config;
        private readonly object _lock = new();

        public TestConfigService(string configDir)
        {
            _configDir = configDir;
            Directory.CreateDirectory(configDir);
        }

        public AppConfig Load()
        {
            lock (_lock)
            {
                return (_config ?? (_config = new AppConfig())).Clone();
            }
        }

        public void Save(AppConfig config)
        {
            lock (_lock)
            {
                _config = config.Clone();
            }
        }

        /// <summary>Set a pre-configured AppConfig for tests.</summary>
        public void SetConfig(AppConfig config)
        {
            lock (_lock)
            {
                _config = config.Clone();
            }
        }
    }

    /// <summary>
    /// Simple IUploadServiceFactory that returns real upload service instances
    /// without requiring DryIoc container.
    /// </summary>
    public class TestUploadServiceFactory : IUploadServiceFactory
    {
        public IUploadService Create(string storageChoice)
        {
            return storageChoice.ToLowerInvariant() switch
            {
                "imgbb" => new ImgbbUploadService(),
                _ => throw new NotSupportedException($"Upload service '{storageChoice}' not supported in tests")
            };
        }
    }

    /// <summary>
    /// Helper for creating test images and temp directories.
    /// </summary>
    public static class TestImageHelper
    {
        /// <summary>
        /// Creates a temp directory with a unique name for test isolation.
        /// </summary>
        public static string CreateTempDirectory(string prefix = "TGallery_IntTest")
        {
            var dir = Path.Combine(Path.GetTempPath(),
                $"{prefix}_{Guid.NewGuid().ToString("N")[..8]}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Creates a test JPEG image with a gradient pattern.
        /// The gradient ensures valid perceptual hashing (solid colors produce degenerate hashes).
        /// </summary>
        public static string CreateTestImage(string directory, string name,
            int width = 200, int height = 200, int seed = 42)
        {
            var path = Path.Combine(directory, name);
            using var image = new Image<Rgba32>(width, height);

            var rng = new Random(seed);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var r = (byte)((x * 255 / width + seed * 37) % 256);
                    var g = (byte)((y * 255 / height + seed * 53) % 256);
                    var b = (byte)(((x + y) * 255 / (width + height) + seed * 71) % 256);
                    image[x, y] = new Rgba32(r, g, b, 255);
                }
            }

            image.SaveAsJpeg(path, new JpegEncoder { Quality = 90 });
            return path;
        }

        /// <summary>
        /// Creates a folder structure with test images for gallery integration tests.
        /// Returns the root gallery folder path.
        /// </summary>
        public static string CreateTestGallery(string rootDir, int imageCount = 3)
        {
            var galleryDir = Path.Combine(rootDir, "gallery");
            Directory.CreateDirectory(galleryDir);

            for (var i = 1; i <= imageCount; i++)
            {
                CreateTestImage(galleryDir, $"test_image_{i}.jpg", seed: i * 100);
            }

            return galleryDir;
        }

        /// <summary>
        /// Creates a gallery with a subfolder containing images.
        /// </summary>
        public static string CreateTestGalleryWithSubfolders(string rootDir)
        {
            var galleryDir = Path.Combine(rootDir, "gallery");
            Directory.CreateDirectory(galleryDir);

            CreateTestImage(galleryDir, "root_image.jpg", seed: 1);

            var subDir = Path.Combine(galleryDir, "subfolder");
            Directory.CreateDirectory(subDir);
            CreateTestImage(subDir, "sub_image.jpg", seed: 2);

            return galleryDir;
        }

        /// <summary>
        /// Creates a minimal gallery with exactly one image (for upload tests).
        /// </summary>
        public static string CreateSingleImageGallery(string rootDir)
        {
            var galleryDir = Path.Combine(rootDir, "single_gallery");
            Directory.CreateDirectory(galleryDir);
            CreateTestImage(galleryDir, "upload_test.jpg", width: 100, height: 100, seed: 999);
            return galleryDir;
        }

        /// <summary>
        /// Creates duplicate images (identical content) for duplicate detection tests.
        /// </summary>
        public static string CreateDuplicateGallery(string rootDir)
        {
            var galleryDir = Path.Combine(rootDir, "dup_gallery");
            Directory.CreateDirectory(galleryDir);

            // Create two images with same seed = identical content
            CreateTestImage(galleryDir, "original.jpg", seed: 42);
            CreateTestImage(galleryDir, "duplicate.jpg", seed: 42);
            // A distinct image
            CreateTestImage(galleryDir, "unique.jpg", seed: 9999);

            return galleryDir;
        }

        /// <summary>Clean up a temp directory, ignoring errors.</summary>
        public static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Test implementation of IProcessLauncher that records calls instead of opening a browser.
    /// </summary>
    public class TestProcessLauncher : IProcessLauncher
    {
        public List<string> OpenedUrls { get; } = new();

        public void OpenUrl(string url)
        {
            OpenedUrls.Add(url);
        }
    }

    /// <summary>
    /// Helper to wait for async ViewModel operations that fire via Prism events.
    /// </summary>
    public static class EventWaiter
    {
        /// <summary>
        /// Subscribes to a PubSubEvent and waits until it is published, with a timeout.
        /// Returns the published payload.
        /// </summary>
        public static async Task<T> WaitForEventAsync<T>(IEventAggregator eventAggregator,
            PubSubEvent<T> evt, TimeSpan? timeout = null)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            SubscriptionToken? token = null;

            token = evt.Subscribe(payload =>
            {
                tcs.TrySetResult(payload);
                if (token != null)
                    evt.Unsubscribe(token);
            });

            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var cts = new CancellationTokenSource(effectiveTimeout);
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }

        /// <summary>
        /// Subscribes to a parameterless PubSubEvent and waits until it is published.
        /// </summary>
        public static async Task WaitForEventAsync(IEventAggregator eventAggregator,
            PubSubEvent evt, TimeSpan? timeout = null)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SubscriptionToken? token = null;

            token = evt.Subscribe(() =>
            {
                tcs.TrySetResult(true);
                if (token != null)
                    evt.Unsubscribe(token);
            });

            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var cts = new CancellationTokenSource(effectiveTimeout);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }
    }
}
