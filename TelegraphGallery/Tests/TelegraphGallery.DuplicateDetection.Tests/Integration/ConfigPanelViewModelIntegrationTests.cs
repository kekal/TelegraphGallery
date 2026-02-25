using System;
using System.Collections.Generic;
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
    public class ConfigPanelViewModelIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly TestConfigService _configService;
        private readonly IEventAggregator _eventAggregator;

        public ConfigPanelViewModelIntegrationTests(IntegrationTestFixture _)
        {
            _tempDir = TestImageHelper.CreateTempDirectory("TGallery_ConfigVM_IntTest");
            _configService = new TestConfigService(_tempDir);
            _eventAggregator = IntegrationTestFixture.CreateEventAggregator();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a ConfigPanelViewModel on the WPF Dispatcher thread, which is required
        /// because DispatcherTimer must be created on an STA/Dispatcher thread.
        /// </summary>
        private ConfigPanelViewModel CreateViewModel()
        {
            ConfigPanelViewModel? vm = null;
            Application.Current.Dispatcher.Invoke(
                () => vm = new ConfigPanelViewModel(_configService, _eventAggregator),
                DispatcherPriority.Normal);
            return vm!;
        }

        /// <summary>
        /// Pumps the WPF Dispatcher message queue so that pending DispatcherTimer ticks
        /// and any queued work items are processed before the test continues.
        /// </summary>
        private static void FlushDispatcher()
        {
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Waits past the 500 ms debounce window then flushes the Dispatcher so that
        /// any pending DispatcherTimer Tick callbacks are processed.
        /// </summary>
        private static async Task WaitForDebounceAsync()
        {
            await Task.Delay(700);
            FlushDispatcher();
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_LoadsDefaultConfig()
        {
            // Arrange: TestConfigService starts with a default AppConfig
            var vm = CreateViewModel();

            // Assert — every VM property should reflect the AppConfig default
            Assert.Equal("imgbb", vm.StorageChoice);
            Assert.Equal("", vm.TelegraphAccessToken);
            Assert.Equal("https://my_page", vm.AuthorUrl);
            Assert.Equal("My albums page", vm.HeaderName);
            Assert.Equal("", vm.ImgbbApiKey);
            Assert.Equal("", vm.CyberdropToken);
            Assert.Equal("", vm.CyberdropAlbumId);
            Assert.Equal(5000, vm.MaxWidth);
            Assert.Equal(5000, vm.MaxHeight);
            Assert.Equal(10000, vm.TotalDimensionThreshold);
            Assert.Equal(5_000_000L, vm.MaxFileSize);
            Assert.Equal(2, vm.PauseSeconds);
            Assert.Equal("old", vm.OutputFolder);
            Assert.Equal(5, vm.DuplicateThreshold);
        }

        [Fact]
        public void Constructor_LoadsCustomConfig()
        {
            // Arrange: preset a custom config before the VM is created
            var custom = new AppConfig
            {
                StorageChoice = "cyberdrop",
                TelegraphAccessToken = "tok_abc123",
                AuthorUrl = "https://custom.example.com",
                HeaderName = "Custom Header",
                ImgbbApiKey = "imgbb_key_999",
                CyberdropToken = "cyber_tok_777",
                CyberdropAlbumId = "album_42",
                MaxWidth = 1920,
                MaxHeight = 1080,
                TotalDimensionThreshold = 3000,
                MaxFileSize = 2_000_000L,
                PauseSeconds = 5,
                OutputFolder = "processed",
                DuplicateThreshold = 8,
            };
            _configService.SetConfig(custom);

            var vm = CreateViewModel();

            // Assert — all VM properties must reflect the custom config
            Assert.Equal("cyberdrop", vm.StorageChoice);
            Assert.Equal("tok_abc123", vm.TelegraphAccessToken);
            Assert.Equal("https://custom.example.com", vm.AuthorUrl);
            Assert.Equal("Custom Header", vm.HeaderName);
            Assert.Equal("imgbb_key_999", vm.ImgbbApiKey);
            Assert.Equal("cyber_tok_777", vm.CyberdropToken);
            Assert.Equal("album_42", vm.CyberdropAlbumId);
            Assert.Equal(1920, vm.MaxWidth);
            Assert.Equal(1080, vm.MaxHeight);
            Assert.Equal(3000, vm.TotalDimensionThreshold);
            Assert.Equal(2_000_000L, vm.MaxFileSize);
            Assert.Equal(5, vm.PauseSeconds);
            Assert.Equal("processed", vm.OutputFolder);
            Assert.Equal(8, vm.DuplicateThreshold);
        }

        [Fact]
        public void StorageChoices_ContainsExpectedValues()
        {
            var vm = CreateViewModel();

            Assert.NotNull(vm.StorageChoices);
            Assert.Contains("imgbb", vm.StorageChoices);
            Assert.Contains("cyberdrop", vm.StorageChoices);
            Assert.Contains("ipfs", vm.StorageChoices);
            Assert.Equal(3, vm.StorageChoices.Count);
        }

        [Fact]
        public void SetStorageChoice_UpdatesConfigAndNotifies()
        {
            var vm = CreateViewModel();
            var notifiedProperties = new List<string?>();
            vm.PropertyChanged += (_, e) => notifiedProperties.Add(e.PropertyName);

            // Act
            Application.Current.Dispatcher.Invoke(
                () => vm.StorageChoice = "ipfs",
                DispatcherPriority.Normal);

            // Assert — property is updated and notification was raised
            Assert.Equal("ipfs", vm.StorageChoice);
            Assert.Contains(nameof(vm.StorageChoice), notifiedProperties);
        }

        [Fact]
        public void SetImgbbApiKey_UpdatesValue()
        {
            var vm = CreateViewModel();

            Application.Current.Dispatcher.Invoke(
                () => vm.ImgbbApiKey = "my_new_api_key",
                DispatcherPriority.Normal);

            Assert.Equal("my_new_api_key", vm.ImgbbApiKey);
        }

        [Fact]
        public void SetAllProperties_AllUpdateCorrectly()
        {
            var vm = CreateViewModel();

            Application.Current.Dispatcher.Invoke(() =>
            {
                vm.StorageChoice = "cyberdrop";
                vm.TelegraphAccessToken = "tok_setall";
                vm.AuthorUrl = "https://setall.example.com";
                vm.HeaderName = "SetAll Header";
                vm.ImgbbApiKey = "setall_imgbb_key";
                vm.CyberdropToken = "setall_cyber_tok";
                vm.CyberdropAlbumId = "setall_album";
                vm.MaxWidth = 800;
                vm.MaxHeight = 600;
                vm.TotalDimensionThreshold = 1400;
                vm.MaxFileSize = 1_000_000L;
                vm.PauseSeconds = 3;
                vm.OutputFolder = "setall_output";
                vm.DuplicateThreshold = 12;
            }, DispatcherPriority.Normal);

            // Assert each property individually
            Assert.Equal("cyberdrop", vm.StorageChoice);
            Assert.Equal("tok_setall", vm.TelegraphAccessToken);
            Assert.Equal("https://setall.example.com", vm.AuthorUrl);
            Assert.Equal("SetAll Header", vm.HeaderName);
            Assert.Equal("setall_imgbb_key", vm.ImgbbApiKey);
            Assert.Equal("setall_cyber_tok", vm.CyberdropToken);
            Assert.Equal("setall_album", vm.CyberdropAlbumId);
            Assert.Equal(800, vm.MaxWidth);
            Assert.Equal(600, vm.MaxHeight);
            Assert.Equal(1400, vm.TotalDimensionThreshold);
            Assert.Equal(1_000_000L, vm.MaxFileSize);
            Assert.Equal(3, vm.PauseSeconds);
            Assert.Equal("setall_output", vm.OutputFolder);
            Assert.Equal(12, vm.DuplicateThreshold);
        }

        [Fact]
        public void SetProperty_RaisesPropertyChanged()
        {
            var vm = CreateViewModel();

            // Subscribe BEFORE setting the property
            var raisedPropertyNames = new List<string?>();
            vm.PropertyChanged += (_, e) => raisedPropertyNames.Add(e.PropertyName);

            Application.Current.Dispatcher.Invoke(
                () => vm.HeaderName = "Changed Header",
                DispatcherPriority.Normal);

            Assert.Contains(nameof(vm.HeaderName), raisedPropertyNames);
        }

        [Fact]
        public async Task SetProperty_SchedulesSave()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act: set a property to trigger the debounce timer
            Application.Current.Dispatcher.Invoke(
                () => vm.AuthorUrl = "https://debounce-save-test.example.com",
                DispatcherPriority.Normal);

            // Wait for the debounce to fire and flush the Dispatcher
            await WaitForDebounceAsync();

            // Assert: TestConfigService.Save should have been called with the updated config.
            // Load() returns a clone of the internally saved config.
            var saved = _configService.Load();
            Assert.Equal("https://debounce-save-test.example.com", saved.AuthorUrl);
        }

        [Fact]
        public async Task SetMultipleProperties_DebounceCoalesces()
        {
            // Arrange: track how many times Save is called via a counting wrapper
            var saveCallCount = 0;
            var countingService = new CountingConfigService(_configService, () => saveCallCount++);

            ConfigPanelViewModel? vm = null;
            Application.Current.Dispatcher.Invoke(
                () => vm = new ConfigPanelViewModel(countingService, _eventAggregator),
                DispatcherPriority.Normal);

            // Act: rapidly set several properties — each resets the debounce timer
            Application.Current.Dispatcher.Invoke(() =>
            {
                vm!.StorageChoice = "ipfs";
                vm!.ImgbbApiKey = "rapid_key_1";
                vm!.CyberdropToken = "rapid_tok";
                vm!.MaxWidth = 1234;
                vm!.PauseSeconds = 7;
            }, DispatcherPriority.Normal);

            // Wait for debounce to settle and Dispatcher to process the Tick
            await WaitForDebounceAsync();

            // Assert: despite five property changes, Save should have fired exactly once
            Assert.Equal(1, saveCallCount);

            // And the saved values should reflect the final state
            var saved = countingService.Load();
            Assert.Equal("ipfs", saved.StorageChoice);
            Assert.Equal("rapid_key_1", saved.ImgbbApiKey);
            Assert.Equal("rapid_tok", saved.CyberdropToken);
            Assert.Equal(1234, saved.MaxWidth);
            Assert.Equal(7, saved.PauseSeconds);
        }

        [Fact]
        public async Task SaveTriggers_ConfigChangedEvent()
        {
            // Arrange: subscribe to ConfigChangedEvent before triggering a save
            var configChangedEvent = _eventAggregator.GetEvent<ConfigChangedEvent>();
            var receivedConfigs = new List<AppConfig>();
            configChangedEvent.Subscribe(cfg => receivedConfigs.Add(cfg));

            var vm = CreateViewModel();

            // Act: change a property to schedule a save
            Application.Current.Dispatcher.Invoke(
                () => vm.OutputFolder = "event_test_output",
                DispatcherPriority.Normal);

            // Wait for debounce and let Dispatcher process the Tick callback
            await WaitForDebounceAsync();

            // Assert: event was published at least once
            Assert.NotEmpty(receivedConfigs);

            // The published config must carry the updated value
            var published = receivedConfigs[^1];
            Assert.Equal("event_test_output", published.OutputFolder);
        }

        // ── Cleanup ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            TestImageHelper.Cleanup(_tempDir);
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Wraps a <see cref="TestConfigService"/> and increments a counter on every
        /// <see cref="Save"/> call so that tests can verify the debounce coalescing behaviour.
        /// </summary>
        private sealed class CountingConfigService : IConfigService
        {
            private readonly TestConfigService _inner;
            private readonly Action _onSave;

            public CountingConfigService(TestConfigService inner, Action onSave)
            {
                _inner = inner;
                _onSave = onSave;
            }

            public AppConfig Load() => _inner.Load();

            public void Save(AppConfig config)
            {
                _inner.Save(config);
                _onSave();
            }
        }
    }
}
