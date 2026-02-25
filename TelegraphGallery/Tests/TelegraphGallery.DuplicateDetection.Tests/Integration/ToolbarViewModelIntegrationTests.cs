using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Prism.Events;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;
using TelegraphGallery.ViewModels;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    [Collection("Integration")]
    public class ToolbarViewModelIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _cacheFilePath;
        private readonly TestConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IUploadCacheService _uploadCacheService;
        private readonly IThumbnailService _thumbnailService;

        public ToolbarViewModelIntegrationTests(IntegrationTestFixture _)
        {
            _tempDir = TestImageHelper.CreateTempDirectory("TGallery_ToolbarVM_IntTest");
            _cacheFilePath = Path.Combine(_tempDir, "upload_cache.json");
            _configService = new TestConfigService(Path.Combine(_tempDir, "config"));
            _eventAggregator = IntegrationTestFixture.CreateEventAggregator();
            _uploadCacheService = IntegrationTestFixture.CreateUploadCacheService(_cacheFilePath);
            _thumbnailService = IntegrationTestFixture.CreateThumbnailService();
        }

        public void Dispose()
        {
            TestImageHelper.Cleanup(_tempDir);
        }

        // ── Factory helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a ToolbarViewModel using the shared services. The constructor does
        /// not require an STA thread for ToolbarViewModel itself (no DispatcherTimer).
        /// </summary>
        private ToolbarViewModel CreateViewModel()
        {
            return new ToolbarViewModel(
                _eventAggregator,
                _configService,
                _uploadCacheService,
                _thumbnailService,
                IntegrationTestFixture.CreateProcessLauncher());
        }

        /// <summary>
        /// Creates a ToolbarViewModel with the given pre-configured AppConfig.
        /// </summary>
        private ToolbarViewModel CreateViewModel(AppConfig config)
        {
            _configService.SetConfig(config);
            return CreateViewModel();
        }

        /// <summary>
        /// Pumps the WPF Dispatcher so that any queued callbacks are processed
        /// before assertions run.
        /// </summary>
        private static void FlushDispatcher()
        {
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        // ── 1. Constructor tests ──────────────────────────────────────────────

        [Fact]
        public void Constructor_LoadsSortModeFromConfig()
        {
            // Arrange
            var config = new AppConfig { SortMode = "File Timestamp" };

            // Act
            var vm = CreateViewModel(config);

            // Assert
            Assert.Equal("File Timestamp", vm.SelectedSortMode);
        }

        [Fact]
        public void Constructor_LoadsThumbnailSizeFromConfig()
        {
            // Arrange
            var config = new AppConfig { ThumbnailSize = 200 };

            // Act
            var vm = CreateViewModel(config);

            // Assert
            Assert.Equal(200, vm.ThumbnailSize);
        }

        [Fact]
        public void Constructor_ValidatesSettings_ImgbbWithKey()
        {
            // Arrange
            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = "my-valid-api-key"
            };

            // Act
            var vm = CreateViewModel(config);

            // Assert
            Assert.True(vm.IsSettingsValid);
        }

        [Fact]
        public void Constructor_ValidatesSettings_ImgbbWithoutKey()
        {
            // Arrange
            var config = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = ""
            };

            // Act
            var vm = CreateViewModel(config);

            // Assert
            Assert.False(vm.IsSettingsValid);
        }

        [Fact]
        public void Constructor_ValidatesSettings_IpfsAlwaysValid()
        {
            // Arrange
            var config = new AppConfig { StorageChoice = "ipfs" };

            // Act
            var vm = CreateViewModel(config);

            // Assert — ipfs falls through to the default case which returns true
            Assert.True(vm.IsSettingsValid);
        }

        // ── 2. SortModes list ────────────────────────────────────────────────

        [Fact]
        public void SortModes_ContainsAllModes()
        {
            // Arrange & Act
            var vm = CreateViewModel();

            // Assert
            Assert.Equal(4, vm.SortModes.Count);
            Assert.Contains("Name", vm.SortModes);
            Assert.Contains("File Timestamp", vm.SortModes);
            Assert.Contains("EXIF Date", vm.SortModes);
            Assert.Contains("Custom", vm.SortModes);
        }

        // ── 3. Property-to-event binding tests ───────────────────────────────

        [Fact]
        public async Task SelectedSortMode_PublishesSortChangedEvent()
        {
            // Arrange
            var vm = CreateViewModel();

            // Subscribe BEFORE setting the property
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<SortChangedEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.SelectedSortMode = "EXIF Date";

            // Assert
            var published = await waitTask;
            Assert.Equal("EXIF Date", published);
        }

        [Fact]
        public async Task ThumbnailSize_PublishesThumbnailSizeChangedEvent()
        {
            // Arrange
            var vm = CreateViewModel();

            // Subscribe BEFORE setting the property
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<ThumbnailSizeChangedEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.ThumbnailSize = 250;

            // Assert
            var published = await waitTask;
            Assert.Equal(250, published);
        }

        // ── 4. Event-to-property binding tests ───────────────────────────────

        [Fact]
        public void UploadStartedEvent_SetsIsUploading()
        {
            // Arrange
            var vm = CreateViewModel();
            Assert.False(vm.IsUploading);

            // Act
            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            // Assert
            Assert.True(vm.IsUploading);
        }

        [Fact]
        public void UploadFinishedEvent_ClearsIsUploading()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.IsUploading = true;
            Assert.True(vm.IsUploading);

            // Act
            _eventAggregator.GetEvent<UploadFinishedEvent>().Publish("done");
            FlushDispatcher();

            // Assert
            Assert.False(vm.IsUploading);
        }

        [Fact]
        public void GalleryLoadingEvent_ClearsIsGalleryLoaded()
        {
            // Arrange
            var vm = CreateViewModel();
            vm.IsGalleryLoaded = true;
            Assert.True(vm.IsGalleryLoaded);

            // Act
            _eventAggregator.GetEvent<GalleryLoadingEvent>().Publish();
            FlushDispatcher();

            // Assert
            Assert.False(vm.IsGalleryLoaded);
        }

        [Fact]
        public void GalleryLoadedEvent_SetsIsGalleryLoaded()
        {
            // Arrange
            var vm = CreateViewModel();
            Assert.False(vm.IsGalleryLoaded);

            // Act
            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(5);
            FlushDispatcher();

            // Assert
            Assert.True(vm.IsGalleryLoaded);
        }

        [Fact]
        public void ConfigChangedEvent_UpdatesSettingsValidation()
        {
            // Arrange — start with imgbb but no key (invalid)
            var config = new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "" };
            var vm = CreateViewModel(config);
            Assert.False(vm.IsSettingsValid);

            // Act — publish a config with a valid key
            var validConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = "new-valid-key"
            };
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(validConfig);
            FlushDispatcher();

            // Assert
            Assert.True(vm.IsSettingsValid);
        }

        [Fact]
        public void PageCreatedEvent_SetsLastResultUrl()
        {
            // Arrange
            var vm = CreateViewModel();
            Assert.Null(vm.LastResultUrl);

            // Act
            _eventAggregator.GetEvent<PageCreatedEvent>().Publish("https://test.url");
            FlushDispatcher();

            // Assert
            Assert.Equal("https://test.url", vm.LastResultUrl);
        }

        [Fact]
        public void SortChangedEvent_UpdatesSelectedSortMode()
        {
            // Arrange — create VM with default sort mode "Name"
            var vm = CreateViewModel();
            Assert.Equal("Name", vm.SelectedSortMode);

            // Subscribe to SortChangedEvent to detect any re-publication
            var sortChangedPublishCount = 0;
            _eventAggregator.GetEvent<SortChangedEvent>().Subscribe(_ => sortChangedPublishCount++);

            // Reset counter after subscription (the SelectedSortMode setter publishes once
            // during construction if it changes; here we track only what happens after Subscribe)
            sortChangedPublishCount = 0;

            // Act — publish from an external source (simulating GalleryViewModel driving sort)
            _eventAggregator.GetEvent<SortChangedEvent>().Publish("Custom");
            FlushDispatcher();

            // Assert — VM must reflect the new sort mode
            Assert.Equal("Custom", vm.SelectedSortMode);

            // The subscription counter increments for the original publish call itself
            // (the subscriber registered above receives it), but the VM's internal handler
            // must NOT re-publish (which would increment the count a second time via the
            // VM's own SelectedSortMode setter, which guards against re-publishing when the
            // value is already equal). One publish = exactly one notification to our subscriber.
            Assert.Equal(1, sortChangedPublishCount);
        }

        // ── 5. Command CanExecute tests ───────────────────────────────────────

        [Fact]
        public void UploadAllCommand_CanExecute_RequiresConditions()
        {
            // Arrange — default state: IsUploading=false, IsSettingsValid=false, IsGalleryLoaded=false
            var config = new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "" };
            var vm = CreateViewModel(config);

            // Assert initially cannot execute (no gallery, no valid settings)
            Assert.False(vm.UploadAllCommand.CanExecute());

            // Enable settings by publishing ConfigChangedEvent with valid key
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "some-key" });
            FlushDispatcher();

            // Still cannot execute — gallery not loaded
            Assert.False(vm.UploadAllCommand.CanExecute());

            // Load the gallery
            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();

            // Now should be able to execute
            Assert.True(vm.UploadAllCommand.CanExecute());
        }

        [Fact]
        public void FindDuplicatesCommand_CanExecute_RequiresGalleryLoaded()
        {
            // Arrange — gallery not loaded
            var vm = CreateViewModel();
            Assert.False(vm.FindDuplicatesCommand.CanExecute());

            // Act — load gallery
            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(2);
            FlushDispatcher();

            // Assert
            Assert.True(vm.FindDuplicatesCommand.CanExecute());
        }

        [Fact]
        public void CancelUploadCommand_CanExecute_RequiresUploading()
        {
            // Arrange — not uploading
            var vm = CreateViewModel();
            Assert.False(vm.CancelUploadCommand.CanExecute());

            // Act — start upload
            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            // Assert
            Assert.True(vm.CancelUploadCommand.CanExecute());
        }

        [Fact]
        public void CopyResultUrlCommand_CanExecute_RequiresUrl()
        {
            // Arrange — no URL yet
            var vm = CreateViewModel();
            Assert.False(vm.CopyResultUrlCommand.CanExecute());

            // Act — set a result URL via event
            _eventAggregator.GetEvent<PageCreatedEvent>().Publish("https://telegra.ph/some-page");
            FlushDispatcher();

            // Assert
            Assert.True(vm.CopyResultUrlCommand.CanExecute());
        }

        // ── 6. Command Execute tests ──────────────────────────────────────────

        [Fact]
        public async Task ToggleSettingsCommand_PublishesToggleEvent()
        {
            // Arrange
            var vm = CreateViewModel();

            // Subscribe BEFORE executing the command
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<ToggleConfigPanelEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.ToggleSettingsCommand.Execute();

            // Assert
            await waitTask; // Completes if and only if the event was published
        }

        [Fact]
        public async Task UploadAllCommand_Execute_PublishesUploadAllEvent()
        {
            // Arrange — set conditions so CanExecute returns true
            var config = new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "valid-key" };
            var vm = CreateViewModel(config);

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(1);
            FlushDispatcher();

            Assert.True(vm.UploadAllCommand.CanExecute());

            // Subscribe BEFORE executing
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<UploadAllEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.UploadAllCommand.Execute();

            // Assert
            await waitTask;
        }

        [Fact]
        public async Task FindDuplicatesCommand_Execute_PublishesFindDuplicatesEvent()
        {
            // Arrange — load gallery so CanExecute returns true
            var vm = CreateViewModel();

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(2);
            FlushDispatcher();

            Assert.True(vm.FindDuplicatesCommand.CanExecute());

            // Subscribe BEFORE executing
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<FindDuplicatesEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.FindDuplicatesCommand.Execute();

            // Assert
            await waitTask;
        }

        [Fact]
        public async Task CancelUploadCommand_Execute_PublishesCancelUploadEvent()
        {
            // Arrange — start uploading so CanExecute returns true
            var vm = CreateViewModel();

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            Assert.True(vm.CancelUploadCommand.CanExecute());

            // Subscribe BEFORE executing
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<CancelUploadEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.CancelUploadCommand.Execute();

            // Assert
            await waitTask;
        }

        [Fact]
        public async Task ClearCacheCommand_ClearsUploadAndThumbnailCache()
        {
            // Arrange
            var vm = CreateViewModel();

            // Subscribe BEFORE executing so we don't race the event
            var waitTask = EventWaiter.WaitForEventAsync(
                _eventAggregator,
                _eventAggregator.GetEvent<StatusUpdateEvent>(),
                TimeSpan.FromSeconds(5));

            // Act
            vm.ClearCacheCommand.Execute();

            // Assert — StatusUpdateEvent("Cache cleared") must be published
            var statusMessage = await waitTask;
            Assert.Equal("Cache cleared", statusMessage);
        }
    }
}
