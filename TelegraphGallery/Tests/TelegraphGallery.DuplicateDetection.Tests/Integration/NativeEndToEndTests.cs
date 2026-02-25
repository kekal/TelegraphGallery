using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Moq;
using Prism.Events;
using Prism.Services.Dialogs;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;
using TelegraphGallery.ViewModels;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    [Collection("Integration")]
    public class NativeEndToEndTests : IDisposable
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly string _rootDir;

        public NativeEndToEndTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _rootDir = TestImageHelper.CreateTempDirectory("TGallery_E2E_IntTest");
        }

        public void Dispose() => TestImageHelper.Cleanup(_rootDir);

        private (GalleryViewModel Gallery, ToolbarViewModel Toolbar, StatusBarViewModel StatusBar,
                 IEventAggregator Ea, TestConfigService Config) CreateAllViewModels(AppConfig? config = null)
        {
            var ea = IntegrationTestFixture.CreateEventAggregator();
            var configService = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config"));
            if (config != null) configService.SetConfig(config);

            var cacheFilePath = Path.Combine(_rootDir, "upload_cache.json");
            var uploadCacheService = IntegrationTestFixture.CreateUploadCacheService(cacheFilePath);
            var thumbnailService = IntegrationTestFixture.CreateThumbnailService();

            var gallery = new GalleryViewModel(
                thumbnailService: thumbnailService,
                imageProcessingService: IntegrationTestFixture.CreateImageProcessingService(),
                duplicateDetectionService: IntegrationTestFixture.CreateDuplicateDetectionService(),
                uploadServiceFactory: IntegrationTestFixture.CreateUploadServiceFactory(),
                telegraphService: IntegrationTestFixture.CreateTelegraphService(),
                configService: configService,
                eventAggregator: ea,
                dialogService: new Mock<IDialogService>().Object,
                uploadCacheService: uploadCacheService);

            var toolbar = new ToolbarViewModel(ea, configService, uploadCacheService, thumbnailService, IntegrationTestFixture.CreateProcessLauncher());
            var statusBar = new StatusBarViewModel(ea);

            return (gallery, toolbar, statusBar, ea, configService);
        }

        private static async Task OpenFolderAndWaitAsync(IEventAggregator ea, string folderPath)
        {
            var loaded = EventWaiter.WaitForEventAsync(
                ea, ea.GetEvent<GalleryLoadedEvent>(), TimeSpan.FromSeconds(30));
            ea.GetEvent<OpenFolderEvent>().Publish(folderPath);
            await loaded;
            await Task.Delay(1500);
        }

        private static void FlushDispatcher()
            => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        // ── 1. Gallery load updates Toolbar + StatusBar ─────────────────────
        [Fact]
        public async Task GalleryLoad_UpdatesToolbarAndStatusBar()
        {
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 3);
            var (gallery, toolbar, statusBar, ea, _) = CreateAllViewModels();

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            Assert.True(toolbar.IsGalleryLoaded);
            Assert.NotEmpty(gallery.Items);
            Assert.Equal(3, gallery.Items.Count);
        }

        // ── 2. ConfigChanged updates Toolbar validation ─────────────────────
        [Fact]
        public async Task ConfigChanged_UpdatesToolbarValidation()
        {
            var ea = IntegrationTestFixture.CreateEventAggregator();
            var configService = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config_cfgtest"));

            var invalidConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = string.Empty,
                PauseSeconds = 0
            };
            configService.SetConfig(invalidConfig);

            var cacheFilePath = Path.Combine(_rootDir, "upload_cache_cfgtest.json");
            var uploadCacheService = IntegrationTestFixture.CreateUploadCacheService(cacheFilePath);
            var thumbnailService = IntegrationTestFixture.CreateThumbnailService();

            var toolbar = new ToolbarViewModel(ea, configService, uploadCacheService, thumbnailService, IntegrationTestFixture.CreateProcessLauncher());

            FlushDispatcher();

            Assert.False(toolbar.IsSettingsValid);

            // Publish a valid config
            var validConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = "test_api_key_12345",
                PauseSeconds = 0
            };
            ea.GetEvent<ConfigChangedEvent>().Publish(validConfig);

            await Task.Delay(100);
            FlushDispatcher();

            Assert.True(toolbar.IsSettingsValid);
        }

        // ── 3. Duplicate detection updates StatusBar ────────────────────────
        [Fact]
        public async Task DuplicateDetection_UpdatesStatusBar()
        {
            var galleryDir = TestImageHelper.CreateDuplicateGallery(_rootDir);
            var (gallery, _, statusBar, ea, _) = CreateAllViewModels();

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            Assert.Equal(3, gallery.Items.Count);

            // Subscribe to status events and wait for the final duplicate result
            var tcs = new TaskCompletionSource<string>();
            ea.GetEvent<StatusUpdateEvent>().Subscribe(msg =>
            {
                if (msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("group", StringComparison.OrdinalIgnoreCase))
                    tcs.TrySetResult(msg);
            });

            ea.GetEvent<FindDuplicatesEvent>().Publish();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            Assert.True(completed == tcs.Task,
                $"Timed out waiting for duplicate status. Last status: '{statusBar.StatusText}'");

            var statusText = await tcs.Task;
            Assert.True(
                statusText.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                statusText.Contains("group", StringComparison.OrdinalIgnoreCase),
                $"StatusBar should mention duplicates. Actual: '{statusText}'");
        }

        // ── 4. Full upload workflow tracks progress ─────────────────────────
        [SkippableFact]
        public async Task FullUploadWorkflow_StatusBarTracksProgress()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);

            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = apiKey,
                PauseSeconds = 0
            };

            var (gallery, toolbar, statusBar, ea, _) = CreateAllViewModels(config);

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            var progressWasNonZero = false;

            ea.GetEvent<ProgressUpdateEvent>().Subscribe(p =>
            {
                if (p > 0) progressWasNonZero = true;
            });

            var uploadFinished = EventWaiter.WaitForEventAsync(
                ea, ea.GetEvent<UploadFinishedEvent>(), TimeSpan.FromSeconds(60));

            ea.GetEvent<UploadAllEvent>().Publish();

            await uploadFinished;
            await Task.Delay(1000);
            FlushDispatcher();

            Assert.True(progressWasNonZero, "Progress should have been > 0 during upload.");
            Assert.False(toolbar.IsUploading);
            Assert.Equal(0, statusBar.Progress);
        }

        // ── 5. Gallery loading disables Toolbar commands ────────────────────
        [Fact]
        public void GalleryLoading_DisablesToolbarCommands()
        {
            var (_, toolbar, _, ea, _) = CreateAllViewModels();
            FlushDispatcher();

            Assert.False(toolbar.UploadAllCommand.CanExecute());
            Assert.False(toolbar.FindDuplicatesCommand.CanExecute());

            ea.GetEvent<GalleryLoadingEvent>().Publish();
            FlushDispatcher();

            Assert.False(toolbar.IsGalleryLoaded);
        }

        // ── 6. Sort mode syncs Toolbar to Gallery ───────────────────────────
        [Fact]
        public async Task SortMode_SyncsToolbarToGallery()
        {
            var galleryDir = Path.Combine(_rootDir, "sort_sync_gallery");
            Directory.CreateDirectory(galleryDir);

            var file1 = TestImageHelper.CreateTestImage(galleryDir, "c_image.jpg", seed: 10);
            var file2 = TestImageHelper.CreateTestImage(galleryDir, "a_image.jpg", seed: 11);
            var file3 = TestImageHelper.CreateTestImage(galleryDir, "b_image.jpg", seed: 12);

            var baseTime = DateTime.Now.AddHours(-3);
            File.SetLastWriteTime(file1, baseTime);
            File.SetLastWriteTime(file2, baseTime.AddHours(1));
            File.SetLastWriteTime(file3, baseTime.AddHours(2));

            var (gallery, toolbar, _, ea, _) = CreateAllViewModels();

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            Assert.NotEmpty(gallery.Items);

            toolbar.SelectedSortMode = "File Timestamp";
            await Task.Delay(400);
            FlushDispatcher();

            var timestamps = gallery.Items.Select(i => i.FileTimestamp).ToList();
            var sortedTimestamps = timestamps.OrderBy(t => t).ToList();
            Assert.Equal(sortedTimestamps, timestamps);
            Assert.Equal("File Timestamp", toolbar.SelectedSortMode);
        }

        // ── 7. Cancel during upload resets state ────────────────────────────
        [SkippableFact]
        public async Task CancelDuringUpload_ResetsStatusAndToolbar()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);

            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = apiKey,
                PauseSeconds = 0
            };

            var (_, toolbar, statusBar, ea, _) = CreateAllViewModels(config);

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            var finishedTask = EventWaiter.WaitForEventAsync(
                ea, ea.GetEvent<UploadFinishedEvent>(), TimeSpan.FromSeconds(30));

            ea.GetEvent<UploadAllEvent>().Publish();
            await Task.Delay(50);
            ea.GetEvent<CancelUploadEvent>().Publish();

            await finishedTask;
            await Task.Delay(500);
            FlushDispatcher();

            Assert.False(toolbar.IsUploading);
            Assert.Equal(0, statusBar.Progress);
        }

        // ── 8. ConfigPanel PauseSeconds affects config ──────────────────────
        [Fact]
        public async Task ConfigPanel_PauseSeconds_AffectsConfig()
        {
            var ea = IntegrationTestFixture.CreateEventAggregator();
            var configService = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config_pause"));

            var initialConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = "test_key",
                PauseSeconds = 5
            };
            configService.SetConfig(initialConfig);

            ConfigPanelViewModel? configVm = null;
            Application.Current.Dispatcher.Invoke(
                () => configVm = new ConfigPanelViewModel(configService, ea));

            FlushDispatcher();

            // Update PauseSeconds to 0
            Application.Current.Dispatcher.Invoke(() =>
            {
                configVm!.PauseSeconds = 0;
            });

            await Task.Delay(700);
            FlushDispatcher();

            var savedConfig = configService.Load();
            Assert.Equal(0, savedConfig.PauseSeconds);
        }

        // ── 9. Multiple gallery loads reset state ───────────────────────────
        [Fact]
        public async Task MultipleGalleryLoads_ResetsPreviousState()
        {
            var galleryDirA = TestImageHelper.CreateDuplicateGallery(_rootDir);
            var galleryDirB = TestImageHelper.CreateTestGallery(
                Path.Combine(_rootDir, "GalleryB"), imageCount: 2);

            var (gallery, _, _, ea, _) = CreateAllViewModels();

            // Load gallery A and detect duplicates
            await OpenFolderAndWaitAsync(ea, galleryDirA);
            FlushDispatcher();

            var statusTask = EventWaiter.WaitForEventAsync(
                ea, ea.GetEvent<StatusUpdateEvent>(), TimeSpan.FromSeconds(60));
            ea.GetEvent<FindDuplicatesEvent>().Publish();
            await statusTask;
            await Task.Delay(500);
            FlushDispatcher();

            var hasDuplicateGroups = gallery.Items.Any(i => i.DuplicateGroupId.HasValue);
            Assert.True(hasDuplicateGroups, "Gallery A should have duplicate groups.");

            // Load gallery B
            await OpenFolderAndWaitAsync(ea, galleryDirB);
            FlushDispatcher();

            Assert.All(gallery.Items, i => Assert.Null(i.DuplicateGroupId));
            Assert.Equal(2, gallery.Items.Count);
        }

        // ── 10. PageCreated updates Toolbar URL ─────────────────────────────
        [SkippableFact]
        public async Task PageCreated_UpdatesToolbarUrl()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);

            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = apiKey,
                PauseSeconds = 0,
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? ""
            };

            var (_, toolbar, _, ea, _) = CreateAllViewModels(config);

            await OpenFolderAndWaitAsync(ea, galleryDir);
            FlushDispatcher();

            var pageCreated = EventWaiter.WaitForEventAsync(
                ea, ea.GetEvent<PageCreatedEvent>(), TimeSpan.FromSeconds(120));

            ea.GetEvent<UploadAllEvent>().Publish();

            var pageUrl = await pageCreated;
            await Task.Delay(500);
            FlushDispatcher();

            Assert.Contains("telegra.ph", toolbar.LastResultUrl ?? string.Empty);
        }
    }
}
