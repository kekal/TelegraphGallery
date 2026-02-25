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
    public class NativeGalleryViewModelTests : IDisposable
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly string _rootDir;

        public NativeGalleryViewModelTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _rootDir = TestImageHelper.CreateTempDirectory("TGallery_NativeGVMTest");
        }

        public void Dispose()
        {
            TestImageHelper.Cleanup(_rootDir);
        }

        // ------------------------------------------------------------------ //
        //  Helper: CreateViewModel
        // ------------------------------------------------------------------ //

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
                thumbnailService:            IntegrationTestFixture.CreateThumbnailService(),
                imageProcessingService:      IntegrationTestFixture.CreateImageProcessingService(),
                duplicateDetectionService:   IntegrationTestFixture.CreateDuplicateDetectionService(),
                uploadServiceFactory:        IntegrationTestFixture.CreateUploadServiceFactory(),
                telegraphService:            IntegrationTestFixture.CreateTelegraphService(),
                configService:               configService,
                eventAggregator:             ea,
                dialogService:               new Mock<IDialogService>().Object,
                uploadCacheService:          IntegrationTestFixture.CreateUploadCacheService(cacheFilePath));

            return (vm, ea, configService);
        }

        // ------------------------------------------------------------------ //
        //  Helper: OpenFolderAndWaitAsync
        // ------------------------------------------------------------------ //

        private static async Task OpenFolderAndWaitAsync(
            IEventAggregator ea,
            string folderPath,
            TimeSpan? timeout = null)
        {
            var loaded = EventWaiter.WaitForEventAsync<int>(
                ea,
                ea.GetEvent<GalleryLoadedEvent>(),
                timeout ?? TimeSpan.FromSeconds(30));

            ea.GetEvent<OpenFolderEvent>().Publish(folderPath);
            await loaded;
            await Task.Delay(1500); // allow thumbnails to settle
        }

        // ------------------------------------------------------------------ //
        //  Helper: UploadAndWaitAsync
        // ------------------------------------------------------------------ //

        private static async Task<string> UploadAndWaitAsync(
            IEventAggregator ea,
            TimeSpan? timeout = null)
        {
            var finished = EventWaiter.WaitForEventAsync<string>(
                ea,
                ea.GetEvent<UploadFinishedEvent>(),
                timeout ?? TimeSpan.FromSeconds(120));

            ea.GetEvent<UploadAllEvent>().Publish();
            return await finished;
        }

        // ================================================================== //
        //  1. ThumbnailSizeChanged_UpdatesDisplaySize
        // ================================================================== //

        [Fact]
        public async Task ThumbnailSizeChanged_UpdatesDisplaySize()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 2);
            var (vm, ea, _) = CreateViewModel();

            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.NotEmpty(vm.Items);

            // Act – publish a new thumbnail size (updates display size instantly, no regeneration)
            const int newSize = 200;
            ea.GetEvent<ThumbnailSizeChangedEvent>().Publish(newSize);

            // Assert – display size updates immediately, no debounce needed
            Assert.Equal(newSize, vm.ThumbnailDisplaySize);
            Assert.NotEmpty(vm.Items);
        }

        // ================================================================== //
        //  2. OpenFolder_ReplacesExistingItems
        // ================================================================== //

        [Fact]
        public async Task OpenFolder_ReplacesExistingItems()
        {
            // Arrange – two distinct galleries
            var folderA = Path.Combine(_rootDir, "galleryA");
            Directory.CreateDirectory(folderA);
            TestImageHelper.CreateTestImage(folderA, "a1.jpg", seed: 1);
            TestImageHelper.CreateTestImage(folderA, "a2.jpg", seed: 2);
            TestImageHelper.CreateTestImage(folderA, "a3.jpg", seed: 3);

            var folderB = Path.Combine(_rootDir, "galleryB");
            Directory.CreateDirectory(folderB);
            TestImageHelper.CreateTestImage(folderB, "b1.jpg", seed: 10);

            var (vm, ea, _) = CreateViewModel();

            // Act – open folder A
            await OpenFolderAndWaitAsync(ea, folderA);
            var countA = vm.Items.Count;

            // Act – open folder B; items should be replaced
            await OpenFolderAndWaitAsync(ea, folderB);
            var countB = vm.Items.Count;

            // Assert
            Assert.Equal(3, countA);
            Assert.Equal(1, countB);
            Assert.All(vm.Items, item =>
                Assert.StartsWith(folderB, item.FilePath, StringComparison.OrdinalIgnoreCase));
        }

        // ================================================================== //
        //  3. OpenFolder_SetsCurrentFolderPath
        // ================================================================== //

        [Fact]
        public async Task OpenFolder_SetsCurrentFolderPath()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 1);
            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert
            Assert.False(string.IsNullOrEmpty(vm.CurrentFolderPath));
            Assert.Equal(
                Path.GetFullPath(galleryDir),
                Path.GetFullPath(vm.CurrentFolderPath!),
                StringComparer.OrdinalIgnoreCase);
        }

        // ================================================================== //
        //  4. OpenFolder_GeneratesThumbnails
        // ================================================================== //

        [Fact]
        public async Task OpenFolder_GeneratesThumbnails()
        {
            // Arrange
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 2);
            var (vm, ea, _) = CreateViewModel();

            // Act
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert – every non-video item should have a Thumbnail generated
            Assert.NotEmpty(vm.Items);
            var imageItems = vm.Items.Where(i => !i.IsVideo).ToList();
            Assert.NotEmpty(imageItems);
            Assert.All(imageItems, item => Assert.NotNull(item.Thumbnail));
        }

        // ================================================================== //
        //  5. SortChanged_AppliesSortByExifDate
        // ================================================================== //

        [Fact]
        public async Task SortChanged_AppliesSortByExifDate()
        {
            // Arrange – create images with staggered file timestamps (EXIF dates will be
            // null for synthetic test images, so the VM falls back to FileTimestamp)
            var galleryDir = Path.Combine(_rootDir, "exif_sort_gallery");
            Directory.CreateDirectory(galleryDir);

            var file1 = TestImageHelper.CreateTestImage(galleryDir, "img_c.jpg", seed: 30);
            var file2 = TestImageHelper.CreateTestImage(galleryDir, "img_a.jpg", seed: 10);
            var file3 = TestImageHelper.CreateTestImage(galleryDir, "img_b.jpg", seed: 20);

            var baseTime = DateTime.Now.AddHours(-3);
            File.SetLastWriteTime(file1, baseTime.AddHours(2));
            File.SetLastWriteTime(file2, baseTime);
            File.SetLastWriteTime(file3, baseTime.AddHours(1));

            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.Equal(3, vm.Items.Count);

            // Act
            ea.GetEvent<SortChangedEvent>().Publish("EXIF Date");
            await Task.Delay(300);

            // Assert – items must be ordered oldest → newest by FileTimestamp (fallback)
            var timestamps = vm.Items.Select(i => i.FileTimestamp).ToList();
            for (int i = 1; i < timestamps.Count; i++)
            {
                Assert.True(timestamps[i] >= timestamps[i - 1],
                    $"Item at index {i} has timestamp {timestamps[i]} which is earlier than {timestamps[i - 1]}");
            }
        }

        // ================================================================== //
        //  6. UploadWorkflow_CacheHit_SkipsUpload
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_CacheHit_SkipsUpload()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange – a shared cache file ensures both VM instances see the same cache
            var cacheFilePath = Path.Combine(_rootDir, "shared_upload_cache.json");

            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            // --- First upload ---
            var galleryDir1 = TestImageHelper.CreateSingleImageGallery(_rootDir);
            var ea1 = IntegrationTestFixture.CreateEventAggregator();
            var cs1 = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config1"));
            cs1.SetConfig(config);

            var vm1 = new GalleryViewModel(
                thumbnailService:          IntegrationTestFixture.CreateThumbnailService(),
                imageProcessingService:    IntegrationTestFixture.CreateImageProcessingService(),
                duplicateDetectionService: IntegrationTestFixture.CreateDuplicateDetectionService(),
                uploadServiceFactory:      IntegrationTestFixture.CreateUploadServiceFactory(),
                telegraphService:          IntegrationTestFixture.CreateTelegraphService(),
                configService:             cs1,
                eventAggregator:           ea1,
                dialogService:             new Mock<IDialogService>().Object,
                uploadCacheService:        IntegrationTestFixture.CreateUploadCacheService(cacheFilePath));

            await OpenFolderAndWaitAsync(ea1, galleryDir1);
            var msg1 = await UploadAndWaitAsync(ea1);
            Assert.False(string.IsNullOrEmpty(msg1));

            // --- Second upload: new gallery dir with an image of the same content ---
            var galleryDir2 = Path.Combine(_rootDir, "single_gallery2");
            Directory.CreateDirectory(galleryDir2);
            // Same seed → identical pixel content → same content hash → cache hit
            TestImageHelper.CreateTestImage(galleryDir2, "upload_test.jpg",
                width: 100, height: 100, seed: 999);

            var ea2 = IntegrationTestFixture.CreateEventAggregator();
            var cs2 = (TestConfigService)IntegrationTestFixture.CreateConfigService(
                Path.Combine(_rootDir, "config2"));
            cs2.SetConfig(config);

            var vm2 = new GalleryViewModel(
                thumbnailService:          IntegrationTestFixture.CreateThumbnailService(),
                imageProcessingService:    IntegrationTestFixture.CreateImageProcessingService(),
                duplicateDetectionService: IntegrationTestFixture.CreateDuplicateDetectionService(),
                uploadServiceFactory:      IntegrationTestFixture.CreateUploadServiceFactory(),
                telegraphService:          IntegrationTestFixture.CreateTelegraphService(),
                configService:             cs2,
                eventAggregator:           ea2,
                dialogService:             new Mock<IDialogService>().Object,
                uploadCacheService:        IntegrationTestFixture.CreateUploadCacheService(cacheFilePath));

            await OpenFolderAndWaitAsync(ea2, galleryDir2);

            var statusMessages2 = new System.Collections.Generic.List<string>();
            ea2.GetEvent<StatusUpdateEvent>().Subscribe(m => statusMessages2.Add(m));

            var msg2 = await UploadAndWaitAsync(ea2);

            // Assert – second run completes successfully (cache hit path)
            Assert.False(string.IsNullOrEmpty(msg2));
            // At least one "Cached" status message should have been published
            Assert.True(statusMessages2.Any(m =>
                    m.IndexOf("cached", StringComparison.OrdinalIgnoreCase) >= 0),
                $"Expected a 'cached' status message; got: [{string.Join(", ", statusMessages2)}]");
        }

        // ================================================================== //
        //  7. UploadWorkflow_ExcludedItemsSkipped
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_ExcludedItemsSkipped()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange – two images so we can exclude one and upload the other
            var galleryDir = Path.Combine(_rootDir, "excl_gallery");
            Directory.CreateDirectory(galleryDir);
            TestImageHelper.CreateTestImage(galleryDir, "keep.jpg",    seed: 11);
            TestImageHelper.CreateTestImage(galleryDir, "exclude.jpg", seed: 22);

            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.Equal(2, vm.Items.Count);

            // Exclude the item named "exclude.jpg"
            var excludedItem = vm.Items.First(i => i.FileName == "exclude.jpg");
            excludedItem.IsExcluded = true;

            // Act
            var msg = await UploadAndWaitAsync(ea);

            // Assert – upload completed
            Assert.False(string.IsNullOrEmpty(msg));

            // The excluded file must NOT have been moved to the output folder
            var outputRoot = Path.Combine(galleryDir, config.OutputFolder);
            var allOutputFiles = Directory.Exists(outputRoot)
                ? Directory.GetFiles(outputRoot, "*.*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            Assert.DoesNotContain(allOutputFiles, f =>
                Path.GetFileName(f).Equals("exclude.jpg", StringComparison.OrdinalIgnoreCase));
        }

        // ================================================================== //
        //  8. UploadWorkflow_WithSubfolders_CreatesMultiplePages
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_WithSubfolders_CreatesMultiplePages()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange – gallery with root image + one subfolder image (1 per group = fast)
            var galleryDir = TestImageHelper.CreateTestGalleryWithSubfolders(_rootDir);

            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.True(vm.Items.Count >= 2,
                "Expected at least 2 images across root and subfolder");

            var pageUrls = new System.Collections.Generic.List<string>();
            ea.GetEvent<PageCreatedEvent>().Subscribe(url => pageUrls.Add(url));

            // Act
            var msg = await UploadAndWaitAsync(ea, TimeSpan.FromMinutes(3));

            // Assert – one page per subfolder group (root = "", subfolder = "subfolder")
            Assert.False(string.IsNullOrEmpty(msg));
            Assert.True(pageUrls.Count >= 2,
                $"Expected at least 2 Telegraph pages for 2 subfolder groups, got {pageUrls.Count}");
        }

        // ================================================================== //
        //  9. UploadWorkflow_MovesFilesToOutputFolder
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_MovesFilesToOutputFolder()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange
            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);
            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            // Record the original file name before the upload moves it
            var originalFiles = Directory.GetFiles(galleryDir, "*.jpg", SearchOption.TopDirectoryOnly);
            Assert.Single(originalFiles);
            var originalFileName = Path.GetFileName(originalFiles[0]);

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Act
            var msg = await UploadAndWaitAsync(ea);

            // Assert – file should be inside the output sub-tree
            Assert.False(string.IsNullOrEmpty(msg));
            var outputRoot = Path.Combine(galleryDir, config.OutputFolder);
            Assert.True(Directory.Exists(outputRoot),
                "Output folder must be created during MoveToOutput");
            var movedFiles = Directory.GetFiles(outputRoot, "*.*", SearchOption.AllDirectories);
            Assert.Contains(movedFiles, f =>
                Path.GetFileName(f).Equals(originalFileName, StringComparison.OrdinalIgnoreCase));
        }

        // ================================================================== //
        //  10. UploadWorkflow_WritesResultsTxt
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_WritesResultsTxt()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange
            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);
            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Act
            var msg = await UploadAndWaitAsync(ea);

            // Assert – results.txt is written to the gallery folder (CurrentFolderPath)
            Assert.False(string.IsNullOrEmpty(msg));
            var resultsPath = Path.Combine(galleryDir, "results.txt");
            Assert.True(File.Exists(resultsPath),
                $"Expected results.txt at '{resultsPath}' but it was not found");
            var content = File.ReadAllText(resultsPath);
            Assert.False(string.IsNullOrWhiteSpace(content),
                "results.txt must not be empty after upload");
        }

        // ================================================================== //
        //  11. UploadWorkflow_ImageProcessing_ResizesLargeImage
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_ImageProcessing_ResizesLargeImage()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange – create a large image (6000 x 4000) which exceeds default MaxDimension 5000
            var galleryDir = Path.Combine(_rootDir, "large_img_gallery");
            Directory.CreateDirectory(galleryDir);
            TestImageHelper.CreateTestImage(galleryDir, "large_image.jpg",
                width: 6000, height: 4000, seed: 77);

            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
                // Keep defaults: MaxWidth=5000, MaxHeight=5000
            };

            var statusMessages = new System.Collections.Generic.List<string>();
            var (vm, ea, _) = CreateViewModel(config);
            ea.GetEvent<StatusUpdateEvent>().Subscribe(m => statusMessages.Add(m));

            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.Single(vm.Items);

            // Act
            var msg = await UploadAndWaitAsync(ea);

            // Assert – upload completed (image was processed without error)
            Assert.False(string.IsNullOrEmpty(msg));
            Assert.DoesNotContain("failed", msg, StringComparison.OrdinalIgnoreCase);

            // "Processing image..." status must have been published (proves resize path ran)
            Assert.True(statusMessages.Any(m =>
                    m.IndexOf("Processing image", StringComparison.OrdinalIgnoreCase) >= 0),
                $"Expected 'Processing image' status; got: [{string.Join(", ", statusMessages)}]");
        }

        // ================================================================== //
        //  12. UploadWorkflow_PreventDoubleUpload
        // ================================================================== //

        [SkippableFact]
        public async Task UploadWorkflow_PreventDoubleUpload()
        {
            var apiKey = IntegrationTestFixture.RequireApiKey();

            // Arrange
            var galleryDir = TestImageHelper.CreateSingleImageGallery(_rootDir);
            var config = new AppConfig
            {
                StorageChoice        = "imgbb",
                ImgbbApiKey          = apiKey,
                PauseSeconds         = 0,
                OutputFolder         = "old",
                TelegraphAccessToken = Environment.GetEnvironmentVariable("TELEGRAPH_TOKEN") ?? "",
            };

            var (vm, ea, _) = CreateViewModel(config);
            await OpenFolderAndWaitAsync(ea, galleryDir);

            var startedCount = 0;
            ea.GetEvent<UploadStartedEvent>().Subscribe(() => startedCount++);

            var finishCount = 0;
            ea.GetEvent<UploadFinishedEvent>().Subscribe(_ => finishCount++);

            // Wait for the single expected finish
            var finished = EventWaiter.WaitForEventAsync<string>(
                ea,
                ea.GetEvent<UploadFinishedEvent>(),
                TimeSpan.FromSeconds(120));

            // Act – fire upload twice in rapid succession; second should be a no-op
            ea.GetEvent<UploadAllEvent>().Publish();
            await Task.Delay(50); // let the guard `if (_isUploading) return` take effect
            ea.GetEvent<UploadAllEvent>().Publish();

            await finished;
            await Task.Delay(300); // settle any late events

            // Assert – exactly one upload session started and finished
            Assert.Equal(1, startedCount);
            Assert.Equal(1, finishCount);
        }

        // ================================================================== //
        //  13. FindDuplicates_NoDuplicates_ShowsMessage
        // ================================================================== //

        [Fact]
        public async Task FindDuplicates_NoDuplicates_ShowsMessage()
        {
            // Arrange – gallery of 3 unique images (different seeds)
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 3);
            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.Equal(3, vm.Items.Count);

            // Capture status messages and wait for the scan's completion message
            var statusMessages = new System.Collections.Generic.List<string>();
            var scanDone = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            ea.GetEvent<StatusUpdateEvent>().Subscribe(m =>
            {
                statusMessages.Add(m);
                // The VM publishes "No duplicates found" or "Found N duplicate groups"
                if (m.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0
                    && m.IndexOf("Finding", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    scanDone.TrySetResult(true);
                }
            });

            // Act
            ea.GetEvent<FindDuplicatesEvent>().Publish();

            var completedInTime = await Task.WhenAny(
                scanDone.Task,
                Task.Delay(TimeSpan.FromSeconds(60)));

            Assert.True(completedInTime == scanDone.Task,
                "Duplicate scan did not complete within 60 seconds");

            // Allow UI-thread work to settle
            await Task.Delay(500);

            // Assert – no items should have a DuplicateGroupId assigned
            Assert.All(vm.Items, item => Assert.False(item.DuplicateGroupId.HasValue,
                $"Item '{item.FileName}' was unexpectedly flagged as a duplicate (GroupId={item.DuplicateGroupId})"));

            // The "No duplicates found" message must be present
            Assert.True(
                statusMessages.Any(m =>
                    m.IndexOf("No duplicates found", StringComparison.OrdinalIgnoreCase) >= 0),
                $"Expected 'No duplicates found' status; got: [{string.Join(", ", statusMessages)}]");
        }

        // ================================================================== //
        //  14. IsLoadingThumbnail transitions during gallery load
        // ================================================================== //

        [Fact]
        public async Task OpenFolder_IsLoadingThumbnail_TransitionsTrueThenFalse()
        {
            // Arrange – create a gallery with a few images
            var galleryDir = TestImageHelper.CreateTestGallery(_rootDir, imageCount: 3);
            var (vm, ea, _) = CreateViewModel();

            // Track IsLoadingThumbnail transitions — when we see the "Loading thumbnails"
            // status, the items have been marked with IsLoadingThumbnail = true
            bool sawLoadingTrue = false;
            ea.GetEvent<StatusUpdateEvent>().Subscribe(msg =>
            {
                if (msg.Contains("Loading thumbnails", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in vm.Items)
                    {
                        if (item.IsLoadingThumbnail)
                        {
                            sawLoadingTrue = true;
                            break;
                        }
                    }
                }
            });

            // Act – open the folder and wait for thumbnails to fully settle
            await OpenFolderAndWaitAsync(ea, galleryDir);

            // Assert – we saw IsLoadingThumbnail == true during loading
            Assert.True(sawLoadingTrue,
                "Expected at least one item with IsLoadingThumbnail=true during thumbnail generation");

            // After everything settles, all items should have IsLoadingThumbnail == false
            Assert.All(vm.Items, item =>
                Assert.False(item.IsLoadingThumbnail,
                    $"Item '{item.FileName}' still has IsLoadingThumbnail=true after settling"));

            // Also verify thumbnails were actually generated
            var imageItems = vm.Items.Where(i => !i.IsVideo).ToList();
            Assert.All(imageItems, item => Assert.NotNull(item.Thumbnail));
        }

        // ================================================================== //
        //  15. FindDuplicates groups duplicates at front and reorders Items
        // ================================================================== //

        [Fact]
        public async Task FindDuplicates_WithDuplicates_ReordersItemsToFront()
        {
            // Arrange – create a gallery where duplicate images are separated by unique ones.
            // File names are chosen so alphabetical scan order places them:
            //   a_original.jpg (seed 42), b_unique1.jpg (seed 100), c_duplicate.jpg (seed 42), d_unique2.jpg (seed 200)
            // After duplicate detection the two seed-42 images should be moved to the front.
            var galleryDir = Path.Combine(_rootDir, "reorder_dup_gallery");
            Directory.CreateDirectory(galleryDir);

            TestImageHelper.CreateTestImage(galleryDir, "a_original.jpg", seed: 42);
            TestImageHelper.CreateTestImage(galleryDir, "b_unique1.jpg", seed: 100);
            TestImageHelper.CreateTestImage(galleryDir, "c_duplicate.jpg", seed: 42);
            TestImageHelper.CreateTestImage(galleryDir, "d_unique2.jpg", seed: 200);

            var (vm, ea, _) = CreateViewModel();
            await OpenFolderAndWaitAsync(ea, galleryDir);
            Assert.Equal(4, vm.Items.Count);

            // Record original order — duplicates should NOT already be adjacent at position 0,1
            var originalNames = vm.Items.Select(i => i.FileName).ToList();

            // Wait for the final "Found N duplicate groups" status
            var tcs = new TaskCompletionSource<string>();
            ea.GetEvent<StatusUpdateEvent>().Subscribe(msg =>
            {
                if (msg.Contains("Found", StringComparison.OrdinalIgnoreCase) &&
                    msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                    tcs.TrySetResult(msg);
            });

            // Also capture sort change to verify "Custom" mode is set
            string? capturedSort = null;
            ea.GetEvent<SortChangedEvent>().Subscribe(s => capturedSort = s);

            // Act
            ea.GetEvent<FindDuplicatesEvent>().Publish();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            Assert.True(completed == tcs.Task,
                $"Timed out waiting for duplicate result. Items: [{string.Join(", ", vm.Items.Select(i => i.FileName))}]");

            await Task.Delay(300); // let UI settle

            // Assert – duplicates are grouped at the front
            var duplicatedItems = vm.Items.Where(i => i.DuplicateGroupId.HasValue).ToList();
            Assert.Equal(2, duplicatedItems.Count);

            // Both duplicates must be at index 0 and 1
            var dupIndices = duplicatedItems.Select(d => vm.Items.IndexOf(d)).OrderBy(i => i).ToList();
            Assert.Equal(0, dupIndices[0]);
            Assert.Equal(1, dupIndices[1]);

            // They must share the same group
            Assert.Equal(duplicatedItems[0].DuplicateGroupId, duplicatedItems[1].DuplicateGroupId);

            // Non-duplicates must be after
            var nonDup = vm.Items.Where(i => !i.DuplicateGroupId.HasValue).ToList();
            Assert.Equal(2, nonDup.Count);
            Assert.All(nonDup, nd => Assert.True(vm.Items.IndexOf(nd) >= 2));

            // Sort mode must have been set to "Custom"
            Assert.Equal("Custom", capturedSort);
        }

        // ================================================================== //
        //  16. IsLoadingThumbnail getter returns correct value
        // ================================================================== //

        [Fact]
        public void GalleryItem_IsLoadingThumbnail_GetterSetterWork()
        {
            // Directly test the GalleryItem property getter/setter
            var item = new GalleryItem
            {
                FileName = "test.jpg",
                FilePath = @"C:\fake\test.jpg"
            };

            // Default value
            Assert.False(item.IsLoadingThumbnail);

            // Set to true
            item.IsLoadingThumbnail = true;
            Assert.True(item.IsLoadingThumbnail);

            // Set back to false
            item.IsLoadingThumbnail = false;
            Assert.False(item.IsLoadingThumbnail);
        }
    }
}
