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
    public class NativeToolbarViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _cacheFilePath;
        private readonly TestConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IUploadCacheService _uploadCacheService;
        private readonly IThumbnailService _thumbnailService;
        private readonly TestProcessLauncher _processLauncher;

        public NativeToolbarViewModelTests(IntegrationTestFixture _)
        {
            _tempDir = TestImageHelper.CreateTempDirectory("TGallery_NativeToolbar_IntTest");
            _cacheFilePath = Path.Combine(_tempDir, "upload_cache.json");
            _configService = new TestConfigService(Path.Combine(_tempDir, "config"));
            _eventAggregator = IntegrationTestFixture.CreateEventAggregator();
            _uploadCacheService = IntegrationTestFixture.CreateUploadCacheService(_cacheFilePath);
            _thumbnailService = IntegrationTestFixture.CreateThumbnailService();
            _processLauncher = IntegrationTestFixture.CreateProcessLauncher();
        }

        public void Dispose() => TestImageHelper.Cleanup(_tempDir);

        private ToolbarViewModel CreateViewModel() =>
            new(_eventAggregator, _configService, _uploadCacheService, _thumbnailService, _processLauncher);

        private ToolbarViewModel CreateViewModel(AppConfig config)
        {
            _configService.SetConfig(config);
            return CreateViewModel();
        }

        private static void FlushDispatcher() =>
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        // ── 1. Cyberdrop with token ─────────────────────────────────────────
        [Fact]
        public void Constructor_ValidatesSettings_CyberdropWithToken()
        {
            var config = new AppConfig
            {
                StorageChoice = "cyberdrop",
                CyberdropToken = "my-valid-token"
            };

            var vm = CreateViewModel(config);

            Assert.True(vm.IsSettingsValid);
        }

        // ── 2. Cyberdrop without token ──────────────────────────────────────
        [Fact]
        public void Constructor_ValidatesSettings_CyberdropWithoutToken()
        {
            var config = new AppConfig
            {
                StorageChoice = "cyberdrop",
                CyberdropToken = ""
            };

            var vm = CreateViewModel(config);

            Assert.False(vm.IsSettingsValid);
        }

        // ── 3. FindDuplicates blocked while uploading ───────────────────────
        [Fact]
        public void FindDuplicatesCommand_CannotExecute_WhileUploading()
        {
            var config = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(config);

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();

            Assert.True(vm.IsGalleryLoaded);
            Assert.False(vm.IsUploading);
            Assert.True(vm.FindDuplicatesCommand.CanExecute());

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            Assert.True(vm.IsUploading);
            Assert.False(vm.FindDuplicatesCommand.CanExecute());
        }

        // ── 4. UploadAll blocked while uploading ────────────────────────────
        [Fact]
        public void UploadAllCommand_CannotExecute_WhileUploading()
        {
            var config = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(config);

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();

            Assert.True(vm.IsSettingsValid);
            Assert.True(vm.IsGalleryLoaded);
            Assert.True(vm.UploadAllCommand.CanExecute());

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            Assert.True(vm.IsUploading);
            Assert.False(vm.UploadAllCommand.CanExecute());
        }

        // ── 5. OpenResultUrl requires URL ───────────────────────────────────
        [Fact]
        public void OpenResultUrlCommand_CanExecute_RequiresUrl()
        {
            var vm = CreateViewModel();

            Assert.False(vm.OpenResultUrlCommand.CanExecute());

            _eventAggregator.GetEvent<PageCreatedEvent>().Publish("https://telegra.ph/test-01-01");
            FlushDispatcher();

            Assert.Equal("https://telegra.ph/test-01-01", vm.LastResultUrl);
            Assert.True(vm.OpenResultUrlCommand.CanExecute());
        }

        // ── 6. OpenResultUrl without URL ────────────────────────────────────
        [Fact]
        public void OpenResultUrlCommand_CannotExecute_WithoutUrl()
        {
            var vm = CreateViewModel();

            Assert.True(string.IsNullOrEmpty(vm.LastResultUrl));
            Assert.False(vm.OpenResultUrlCommand.CanExecute());
        }

        // ── 7. ConfigChanged cyberdrop validation ───────────────────────────
        [Fact]
        public void ConfigChangedEvent_CyberdropValidation()
        {
            var initialConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = ""
            };
            var vm = CreateViewModel(initialConfig);

            Assert.False(vm.IsSettingsValid);

            var updatedConfig = new AppConfig
            {
                StorageChoice = "cyberdrop",
                CyberdropToken = "valid-token-abc"
            };
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(updatedConfig);
            FlushDispatcher();

            Assert.True(vm.IsSettingsValid);
        }

        // ── 8. ConfigChanged cyberdrop no token ─────────────────────────────
        [Fact]
        public void ConfigChangedEvent_CyberdropNoToken_Invalid()
        {
            var initialConfig = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(initialConfig);

            Assert.True(vm.IsSettingsValid);

            var updatedConfig = new AppConfig
            {
                StorageChoice = "cyberdrop",
                CyberdropToken = "   "
            };
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(updatedConfig);
            FlushDispatcher();

            Assert.False(vm.IsSettingsValid);
        }

        // ── 9. UploadAll all conditions met ─────────────────────────────────
        [Fact]
        public void UploadAllCommand_CanExecute_AllConditionsMet()
        {
            var config = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(config);

            Assert.False(vm.IsUploading);
            Assert.True(vm.IsSettingsValid);
            Assert.False(vm.IsGalleryLoaded);
            Assert.False(vm.UploadAllCommand.CanExecute());

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();

            Assert.False(vm.IsUploading);
            Assert.True(vm.IsSettingsValid);
            Assert.True(vm.IsGalleryLoaded);
            Assert.True(vm.UploadAllCommand.CanExecute());
        }

        // ── 10. IsUploading disables both commands ──────────────────────────
        [Fact]
        public void IsUploading_DisablesUploadAndDuplicateCommands()
        {
            var config = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(config);

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();

            Assert.True(vm.UploadAllCommand.CanExecute());
            Assert.True(vm.FindDuplicatesCommand.CanExecute());

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();

            Assert.True(vm.IsUploading);
            Assert.False(vm.UploadAllCommand.CanExecute());
            Assert.False(vm.FindDuplicatesCommand.CanExecute());

            _eventAggregator.GetEvent<UploadFinishedEvent>().Publish("done");
            FlushDispatcher();

            Assert.False(vm.IsUploading);
            Assert.True(vm.UploadAllCommand.CanExecute());
            Assert.True(vm.FindDuplicatesCommand.CanExecute());
        }

        // ── 11. Full event lifecycle ────────────────────────────────────────
        [Fact]
        public void FullEventSequence_GalleryLoadUploadFinish()
        {
            var config = new AppConfig { StorageChoice = "ipfs" };
            var vm = CreateViewModel(config);

            // Phase 1: Gallery loading
            _eventAggregator.GetEvent<GalleryLoadingEvent>().Publish();
            FlushDispatcher();
            Assert.False(vm.IsGalleryLoaded);

            // Phase 2: Gallery loaded
            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(5);
            FlushDispatcher();
            Assert.True(vm.IsGalleryLoaded);
            Assert.False(vm.IsUploading);

            // Phase 3: Upload started
            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();
            Assert.True(vm.IsUploading);
            Assert.False(vm.UploadAllCommand.CanExecute());
            Assert.False(vm.FindDuplicatesCommand.CanExecute());
            Assert.True(vm.CancelUploadCommand.CanExecute());

            // Phase 4: Page created
            _eventAggregator.GetEvent<PageCreatedEvent>().Publish("https://telegra.ph/test-gallery-01-01");
            FlushDispatcher();
            Assert.Equal("https://telegra.ph/test-gallery-01-01", vm.LastResultUrl);
            Assert.True(vm.CopyResultUrlCommand.CanExecute());
            Assert.True(vm.OpenResultUrlCommand.CanExecute());

            // Phase 5: Upload finished
            _eventAggregator.GetEvent<UploadFinishedEvent>().Publish("Upload complete");
            FlushDispatcher();
            Assert.False(vm.IsUploading);
            Assert.True(vm.UploadAllCommand.CanExecute());
            Assert.True(vm.FindDuplicatesCommand.CanExecute());
            Assert.False(vm.CancelUploadCommand.CanExecute());
        }

        // ── 12. ClearCache always available ─────────────────────────────────
        [Fact]
        public void ClearCacheCommand_AlwaysCanExecute()
        {
            var vm = CreateViewModel();

            Assert.True(vm.ClearCacheCommand.CanExecute());

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();
            Assert.True(vm.ClearCacheCommand.CanExecute());

            _eventAggregator.GetEvent<UploadFinishedEvent>().Publish("done");
            FlushDispatcher();
            Assert.True(vm.ClearCacheCommand.CanExecute());

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(3);
            FlushDispatcher();
            Assert.True(vm.ClearCacheCommand.CanExecute());
        }

        // ── 13. SortChanged no re-publish ───────────────────────────────────
        [Fact]
        public void SortChangedEvent_DoesNotRePublish()
        {
            var vm = CreateViewModel();

            var externalEventCount = 0;
            _eventAggregator.GetEvent<SortChangedEvent>().Subscribe(
                _ => externalEventCount++);

            externalEventCount = 0;

            _eventAggregator.GetEvent<SortChangedEvent>().Publish("Custom");
            FlushDispatcher();

            Assert.Equal("Custom", vm.SelectedSortMode);
            Assert.Equal(1, externalEventCount);
        }

        // ── 14. Destroy does not throw ──────────────────────────────────────
        [Fact]
        public void Destroy_DoesNotThrow()
        {
            var vm = CreateViewModel();

            var exception = Record.Exception(() => vm.Destroy());

            Assert.Null(exception);
        }

        // ── 15. Switching providers ─────────────────────────────────────────
        [Fact]
        public void ConfigChangedEvent_SwitchingProviders()
        {
            var initialConfig = new AppConfig
            {
                StorageChoice = "imgbb",
                ImgbbApiKey = ""
            };
            var vm = CreateViewModel(initialConfig);
            Assert.False(vm.IsSettingsValid);

            // Switch to ipfs (always valid)
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "ipfs" });
            FlushDispatcher();
            Assert.True(vm.IsSettingsValid);

            // Switch to imgbb with whitespace key (invalid)
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "   " });
            FlushDispatcher();
            Assert.False(vm.IsSettingsValid);

            // Switch to imgbb with valid key
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "imgbb", ImgbbApiKey = "my-imgbb-key" });
            FlushDispatcher();
            Assert.True(vm.IsSettingsValid);

            // Switch to cyberdrop with token (valid)
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "cyberdrop", CyberdropToken = "cyberdrop-token-xyz" });
            FlushDispatcher();
            Assert.True(vm.IsSettingsValid);

            // Switch to cyberdrop without token (invalid)
            _eventAggregator.GetEvent<ConfigChangedEvent>().Publish(
                new AppConfig { StorageChoice = "cyberdrop", CyberdropToken = "" });
            FlushDispatcher();
            Assert.False(vm.IsSettingsValid);
        }

        // ── 16. OpenFolderCommand always CanExecute ───────────────────────────
        [Fact]
        public void OpenFolderCommand_AlwaysCanExecute()
        {
            var vm = CreateViewModel();

            // OpenFolderCommand has no CanExecute predicate — always enabled
            Assert.True(vm.OpenFolderCommand.CanExecute());

            // Still true during upload
            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            FlushDispatcher();
            Assert.True(vm.OpenFolderCommand.CanExecute());

            _eventAggregator.GetEvent<UploadFinishedEvent>().Publish("done");
            FlushDispatcher();
            Assert.True(vm.OpenFolderCommand.CanExecute());
        }

        // ── 17. CopyResultUrl copies to clipboard ────────────────────────────
        [Fact]
        public void OnCopyResultUrl_CopiesUrlToClipboard()
        {
            var vm = CreateViewModel();

            // Set a URL via the PageCreated event
            const string testUrl = "https://telegra.ph/test-copy-01-01";
            _eventAggregator.GetEvent<PageCreatedEvent>().Publish(testUrl);
            FlushDispatcher();

            Assert.True(vm.CopyResultUrlCommand.CanExecute());

            // Clipboard requires STA thread — invoke on the Application dispatcher
            Application.Current.Dispatcher.Invoke(() =>
            {
                vm.CopyResultUrlCommand.Execute();
            });

            // Verify clipboard content on the STA thread
            string clipboardText = null!;
            Application.Current.Dispatcher.Invoke(() =>
            {
                clipboardText = Clipboard.GetText();
            });
            Assert.Equal(testUrl, clipboardText);
        }

        // ── 18. CopyResultUrl no-op when URL is empty ────────────────────────
        [Fact]
        public void OnCopyResultUrl_NoUrl_DoesNotThrow()
        {
            var vm = CreateViewModel();

            // CanExecute is false, but calling the method body with empty URL should be safe
            Assert.False(vm.CopyResultUrlCommand.CanExecute());
            // Directly test that the guard does not throw
            var exception = Record.Exception(() => vm.CopyResultUrlCommand.Execute());
            Assert.Null(exception);
        }

        // ── 19. OpenResultUrl passes URL to process launcher ─────────────────
        [Fact]
        public void OnOpenResultUrl_WithUrl_PassesUrlToLauncher()
        {
            var vm = CreateViewModel();

            const string testUrl = "https://telegra.ph/test-open-01-01";
            _eventAggregator.GetEvent<PageCreatedEvent>().Publish(testUrl);
            FlushDispatcher();

            Assert.True(vm.OpenResultUrlCommand.CanExecute());
            Assert.Empty(_processLauncher.OpenedUrls);

            vm.OpenResultUrlCommand.Execute();

            Assert.Single(_processLauncher.OpenedUrls);
            Assert.Equal(testUrl, _processLauncher.OpenedUrls[0]);
        }

        // ── 20. OpenResultUrl no-op when URL is empty ────────────────────────
        [Fact]
        public void OnOpenResultUrl_NoUrl_DoesNotCallLauncher()
        {
            var vm = CreateViewModel();

            Assert.True(string.IsNullOrEmpty(vm.LastResultUrl));
            Assert.False(vm.OpenResultUrlCommand.CanExecute());

            vm.OpenResultUrlCommand.Execute();

            Assert.Empty(_processLauncher.OpenedUrls);
        }
    }
}
