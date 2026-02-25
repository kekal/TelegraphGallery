using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Prism.Events;
using Prism.Services.Dialogs;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.ViewModels;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    [Collection("Integration")]
    public class GalleryViewModelIntegrationTests : IDisposable
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly string _rootDir;

        public GalleryViewModelIntegrationTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _rootDir = TestImageHelper.CreateTempDirectory("TGallery_GVMIntTest");
        }

        public void Dispose()
        {
            TestImageHelper.Cleanup(_rootDir);
        }

        // ── Factory helper ────────────────────────────────────────────────

        private (GalleryViewModel Vm, IEventAggregator Ea, TestConfigService ConfigService) CreateViewModel(
            AppConfig? config = null)
        {
            var ea = IntegrationTestFixture.CreateEventAggregator();
            var configService = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config"));

            if (config != null)
                configService.SetConfig(config);

            var cacheFilePath = Path.Combine(_rootDir, "upload_cache.json");

            var vm = new GalleryViewModel(
                thumbnailService: IntegrationTestFixture.CreateThumbnailService(),
                imageProcessingService: IntegrationTestFixture.CreateImageProcessingService(),
                duplicateDetectionService: IntegrationTestFixture.CreateDuplicateDetectionService(),
                uploadServiceFactory: IntegrationTestFixture.CreateUploadServiceFactory(),
                telegraphService: IntegrationTestFixture.CreateTelegraphService(),
                configService: configService,
                eventAggregator: ea,
                dialogService: new Mock<IDialogService>().Object,
                uploadCacheService: IntegrationTestFixture.CreateUploadCacheService(cacheFilePath));

            return (vm, ea, configService);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Publishes OpenFolderEvent and waits for GalleryLoadedEvent to confirm scan is done.
        /// </summary>
        private static async Task OpenFolderAndWaitAsync(
            IEventAggregator ea, string folderPath, TimeSpan? timeout = null)
        {
            var loaded = EventWaiter.WaitForEventAsync(
                ea,
                ea.GetEvent<GalleryLoadedEvent>(),
                timeout ?? TimeSpan.FromSeconds(30));

            ea.GetEvent<OpenFolderEvent>().Publish(folderPath);

            await loaded;

            // Allow Dispatcher-posted thumbnail assignments to settle before assertions.
            await Task.Delay(1500);
        }

        // ── Tests ─────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_LoadsConfigAndInitializes()
        {
            // Arrange
            var config = new AppConfig { ThumbnailSize = 220 };

            // Act
            var (vm, _, _) = CreateViewModel(config);

            // Assert
            Assert.Equal(220, vm.ThumbnailDisplaySize);
            Assert.NotNull(vm.Items);
            Assert.Empty(vm.Items);
            Assert.NotNull(vm.ToggleExcludeCommand);
        }

        [Fact]
        public async Task OpenFolder_ScansAndPopulatesItems()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 3);
            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert
            Assert.Equal(3, vm.Items.Count);
            Assert.All(vm.Items, item => Assert.False(string.IsNullOrEmpty(item.FilePath)));
            Assert.All(vm.Items, item => Assert.True(File.Exists(item.FilePath)));
        }

        [Fact]
        public async Task OpenFolder_FindsImagesInSubfolders()
        {
            // Arrange — gallery with root image + subfolder image
            var galleryDir = TestImageHelper.CreateTestGalleryWithSubfolders(_rootDir);
            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert
            Assert.Equal(2, vm.Items.Count);

            var rootItem = vm.Items.FirstOrDefault(i => i.FileName == "root_image.jpg");
            Assert.NotNull(rootItem);
            Assert.Equal("", rootItem!.SubFolder);

            var subItem = vm.Items.FirstOrDefault(i => i.FileName == "sub_image.jpg");
            Assert.NotNull(subItem);
            Assert.Equal("subfolder", subItem!.SubFolder);
        }

        [Fact]
        public async Task OpenFolder_ExcludesBlockedFolders()
        {
            // Arrange — gallery with a "temp" subfolder whose images must be ignored
            var galleryDir = Path.Combine(_rootDir, "blocked_gallery");
            Directory.CreateDirectory(galleryDir);
            TestImageHelper.CreateTestImage(galleryDir, "allowed_image.jpg", seed: 1);

            var tempDir = Path.Combine(galleryDir, "temp");
            Directory.CreateDirectory(tempDir);
            TestImageHelper.CreateTestImage(tempDir, "temp_image.jpg", seed: 2);

            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert — only the root-level image is loaded; the temp subfolder is excluded
            var onlyItem = Assert.Single(vm.Items);
            Assert.Equal("allowed_image.jpg", onlyItem.FileName);
        }

        [Fact]
        public async Task OpenFolder_IgnoresNonImageFiles()
        {
            // Arrange — directory with a .jpg and a .txt that must be ignored
            var galleryDir = Path.Combine(_rootDir, "mixed_gallery");
            Directory.CreateDirectory(galleryDir);
            TestImageHelper.CreateTestImage(galleryDir, "photo.jpg", seed: 77);
            File.WriteAllText(Path.Combine(galleryDir, "readme.txt"), "ignore me");

            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert
            var onlyImage = Assert.Single(vm.Items);
            Assert.Equal("photo.jpg", onlyImage.FileName);
        }

        [Fact]
        public async Task SortChanged_AppliesSortByName()
        {
            // Arrange — create images whose filenames sort differently than alphabetical
            var galleryDir = Path.Combine(_rootDir, "sort_name_gallery");
            Directory.CreateDirectory(galleryDir);
            TestImageHelper.CreateTestImage(galleryDir, "zebra.jpg", seed: 1);
            TestImageHelper.CreateTestImage(galleryDir, "alpha.jpg", seed: 2);
            TestImageHelper.CreateTestImage(galleryDir, "mango.jpg", seed: 3);

            var (vm, ea, _) = CreateViewModel(new AppConfig { SortMode = "Name" });
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Act
            ea.GetEvent<SortChangedEvent>().Publish("Name");
            await Task.Delay(200);

            // Assert — items must be in ascending name order
            var names = vm.Items.Select(i => i.FileName).ToList();
            var expected = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(expected, names);
        }

        [Fact]
        public async Task SortChanged_AppliesSortByTimestamp()
        {
            // Arrange — create images and touch their LastWriteTime to control order
            var galleryDir = Path.Combine(_rootDir, "sort_ts_gallery");
            Directory.CreateDirectory(galleryDir);

            var file1 = TestImageHelper.CreateTestImage(galleryDir, "first.jpg", seed: 10);
            var file2 = TestImageHelper.CreateTestImage(galleryDir, "second.jpg", seed: 11);
            var file3 = TestImageHelper.CreateTestImage(galleryDir, "third.jpg", seed: 12);

            // Stagger the timestamps so the expected order is predictable
            var baseTime = DateTime.Now.AddHours(-3);
            File.SetLastWriteTime(file1, baseTime);
            File.SetLastWriteTime(file2, baseTime.AddHours(1));
            File.SetLastWriteTime(file3, baseTime.AddHours(2));

            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Act
            ea.GetEvent<SortChangedEvent>().Publish("File Timestamp");
            await Task.Delay(200);

            // Assert — items ordered oldest → newest
            var timestamps = vm.Items.Select(i => i.FileTimestamp).ToList();
            var sortedTimestamps = timestamps.OrderBy(t => t).ToList();
            Assert.Equal(sortedTimestamps, timestamps);
        }

        [Fact]
        public async Task SortChanged_CustomMode_PreservesOrder()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 3);
            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Capture the current order after initial load
            var orderBeforeCustom = vm.Items.Select(i => i.FileName).ToList();

            // Act — switching to Custom must not reorder items
            ea.GetEvent<SortChangedEvent>().Publish("Custom");
            await Task.Delay(200);

            // Assert
            var orderAfterCustom = vm.Items.Select(i => i.FileName).ToList();
            Assert.Equal(orderBeforeCustom, orderAfterCustom);
        }

        [Fact]
        public async Task ToggleExcludeCommand_TogglesItemExclusion()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 2);
            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);

            var item = vm.Items[0];
            Assert.False(item.IsExcluded);

            // Act — toggle on
            vm.ToggleExcludeCommand.Execute(item);

            // Assert — now excluded
            Assert.True(item.IsExcluded);

            // Act — toggle off
            vm.ToggleExcludeCommand.Execute(item);

            // Assert — back to included
            Assert.False(item.IsExcluded);
        }

        [Fact]
        public async Task FindDuplicates_IdentifiesDuplicateImages()
        {
            // Arrange — gallery with two identical images (same seed) and one unique
            var galleryDir = TestImageHelper.CreateDuplicateGallery(_rootDir);
            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);

            Assert.Equal(3, vm.Items.Count);

            // Register for the status update that signals completion
            var statusTask = EventWaiter.WaitForEventAsync(
                ea,
                ea.GetEvent<StatusUpdateEvent>(),
                TimeSpan.FromSeconds(60));

            // Act
            ea.GetEvent<FindDuplicatesEvent>().Publish();

            // Wait for status update confirming duplicate scan finished
            await statusTask;
            // Give the UI thread a moment to apply DuplicateGroupId values
            await Task.Delay(500);

            // Assert — the two identical images share a non-null DuplicateGroupId
            var duplicated = vm.Items.Where(i => i.DuplicateGroupId.HasValue).ToList();
            Assert.Equal(2, duplicated.Count);
            Assert.Equal(duplicated[0].DuplicateGroupId, duplicated[1].DuplicateGroupId);

            // The unique image must have no group
            var unique = vm.Items.FirstOrDefault(i => i.FileName == "unique.jpg");
            Assert.NotNull(unique);
            Assert.Null(unique!.DuplicateGroupId);
        }

        [Fact]
        public async Task CancelUpload_StopsUploadProcess()
        {
            // Arrange — a gallery with one image; upload will be cancelled immediately
            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);
            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = "invalid_key_for_cancel_test",
                PauseSeconds = 0
            };
            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);

            var finishedTask = EventWaiter.WaitForEventAsync(
                ea,
                ea.GetEvent<UploadFinishedEvent>(),
                TimeSpan.FromSeconds(30));

            // Act — start upload and immediately cancel
            ea.GetEvent<UploadAllEvent>().Publish();
            await Task.Delay(50);
            ea.GetEvent<CancelUploadEvent>().Publish();

            // Wait for the upload workflow to acknowledge termination
            var finishedMessage = await finishedTask;

            // Assert — workflow must have been cancelled or completed (not hang)
            Assert.NotNull(finishedMessage);
        }

        [Fact]
        public void Destroy_DisposesResources()
        {
            // Arrange
            var (vm, _, _) = CreateViewModel();

            // Act & Assert — must not throw
            var ex = Record.Exception(() => vm.Destroy());
            Assert.Null(ex);
        }

        [SkippableFact]
        public async Task UploadWorkflow_SingleImage_UploadsAndCreatesPage()
        {
            // Arrange
            var apiKey = IntegrationTestFixture.RequireApiKey();

            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);

            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = apiKey,
                PauseSeconds = 0,
                OutputFolder = "old",
                // Telegraph requires a valid access token; reuse the API key slot if
                // a dedicated env var is not set — the test validates the URL shape only.
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? ""
            };

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);

            Assert.Single(vm.Items);

            // Register listeners before triggering so we don't race
            var pageCreatedTask = EventWaiter.WaitForEventAsync(
                ea,
                ea.GetEvent<PageCreatedEvent>(),
                TimeSpan.FromSeconds(120));

            var uploadFinishedTask = EventWaiter.WaitForEventAsync(
                ea,
                ea.GetEvent<UploadFinishedEvent>(),
                TimeSpan.FromSeconds(120));

            // Act
            ea.GetEvent<UploadAllEvent>().Publish();

            // Wait for the Telegraph page URL to be published
            var pageUrl = await pageCreatedTask;
            await uploadFinishedTask;

            // Assert — URL must point to Telegraph
            Assert.False(string.IsNullOrEmpty(pageUrl), "PageCreatedEvent must carry a URL");
            Assert.Contains("telegra.ph", pageUrl);

            // Assert — file must have been moved to the output folder
            var outputDir = Path.Combine(galleryDir, config.OutputFolder);
            Assert.True(Directory.Exists(outputDir), "Output folder must be created");
            var movedFiles = Directory.GetFiles(outputDir, "*.jpg", SearchOption.AllDirectories);
            Assert.NotEmpty(movedFiles);

            // Assert — results.txt written in the gallery folder
            var resultsPath = Path.Combine(galleryDir, "results.txt");
            Assert.True(File.Exists(resultsPath), "results.txt must be written after upload");
            var content = await File.ReadAllTextAsync(resultsPath);
            Assert.Contains("telegra.ph", content);
        }
    }
}
