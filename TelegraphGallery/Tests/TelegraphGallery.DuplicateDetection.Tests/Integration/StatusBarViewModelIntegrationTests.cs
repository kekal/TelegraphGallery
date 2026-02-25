using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Prism.Events;
using TelegraphGallery.Events;
using TelegraphGallery.ViewModels;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    [Collection("Integration")]
    public class StatusBarViewModelIntegrationTests
    {
        // ── Factory helper ────────────────────────────────────────────────

        private static (StatusBarViewModel Vm, IEventAggregator Ea) CreateViewModel()
        {
            var ea = IntegrationTestFixture.CreateEventAggregator();
            var vm = new StatusBarViewModel(ea);
            return (vm, ea);
        }

        /// <summary>
        /// Flushes all pending Dispatcher operations so that ThreadOption.UIThread
        /// subscriptions have run before assertions are made.
        /// </summary>
        private static async Task FlushDispatcherAsync()
        {
            await Task.Delay(100);
        }

        // ── Tests ─────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_InitializesWithDefaultValues()
        {
            // Act
            var (vm, _) = CreateViewModel();

            // Assert
            Assert.Equal("Ready", vm.StatusText);
            Assert.Equal(0.0, vm.Progress);
            Assert.False(vm.IsProgressVisible);
        }

        [Fact]
        public async Task StatusUpdateEvent_UpdatesStatusText()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            // Act
            ea.GetEvent<StatusUpdateEvent>().Publish("Loading...");
            await FlushDispatcherAsync();

            // Assert
            Assert.Equal("Loading...", vm.StatusText);
        }

        [Fact]
        public async Task StatusUpdateEvent_MultipleUpdates_KeepsLatest()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            // Act — publish several updates in sequence
            ea.GetEvent<StatusUpdateEvent>().Publish("First");
            await FlushDispatcherAsync();

            ea.GetEvent<StatusUpdateEvent>().Publish("Second");
            await FlushDispatcherAsync();

            ea.GetEvent<StatusUpdateEvent>().Publish("Third");
            await FlushDispatcherAsync();

            // Assert — only the last update should be visible
            Assert.Equal("Third", vm.StatusText);
        }

        [Fact]
        public async Task ProgressUpdateEvent_UpdatesProgress()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            // Act
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.5);
            await FlushDispatcherAsync();

            // Assert
            Assert.Equal(0.5, vm.Progress);
        }

        [Fact]
        public async Task ProgressUpdateEvent_PositiveValue_ShowsProgress()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            // Act
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.3);
            await FlushDispatcherAsync();

            // Assert
            Assert.True(vm.IsProgressVisible);
        }

        [Fact]
        public async Task ProgressUpdateEvent_Zero_HidesProgress()
        {
            // Arrange — first bring progress to a visible state
            var (vm, ea) = CreateViewModel();

            ea.GetEvent<ProgressUpdateEvent>().Publish(0.5);
            await FlushDispatcherAsync();

            Assert.True(vm.IsProgressVisible);

            // Act — publish zero
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.0);
            await FlushDispatcherAsync();

            // Assert
            Assert.False(vm.IsProgressVisible);
        }

        [Fact]
        public async Task UploadFinishedEvent_ResetsProgress()
        {
            // Arrange — put the view model into a visible-progress state
            var (vm, ea) = CreateViewModel();

            ea.GetEvent<ProgressUpdateEvent>().Publish(0.7);
            await FlushDispatcherAsync();

            Assert.Equal(0.7, vm.Progress);
            Assert.True(vm.IsProgressVisible);

            // Act
            ea.GetEvent<UploadFinishedEvent>().Publish("done");
            await FlushDispatcherAsync();

            // Assert
            Assert.Equal(0.0, vm.Progress);
            Assert.False(vm.IsProgressVisible);
        }

        [Fact]
        public async Task GalleryLoadedEvent_ResetsProgress()
        {
            // Arrange — put the view model into a visible-progress state
            var (vm, ea) = CreateViewModel();

            ea.GetEvent<ProgressUpdateEvent>().Publish(0.5);
            await FlushDispatcherAsync();

            Assert.Equal(0.5, vm.Progress);
            Assert.True(vm.IsProgressVisible);

            // Act
            ea.GetEvent<GalleryLoadedEvent>().Publish(10);
            await FlushDispatcherAsync();

            // Assert
            Assert.Equal(0.0, vm.Progress);
            Assert.False(vm.IsProgressVisible);
        }

        [Fact]
        public async Task PropertyChanged_StatusText_FiresNotification()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            var changedProperties = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            // Act
            ea.GetEvent<StatusUpdateEvent>().Publish("Scanning...");
            await FlushDispatcherAsync();

            // Assert
            Assert.Contains("StatusText", changedProperties);
        }

        [Fact]
        public async Task PropertyChanged_Progress_FiresNotification()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            var changedProperties = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            // Act
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.6);
            await FlushDispatcherAsync();

            // Assert
            Assert.Contains("Progress", changedProperties);
        }

        [Fact]
        public async Task FullWorkflow_UploadSequence()
        {
            // Arrange
            var (vm, ea) = CreateViewModel();

            // Step 1 — status message arrives indicating upload has started
            ea.GetEvent<StatusUpdateEvent>().Publish("Uploading images...");
            await FlushDispatcherAsync();

            Assert.Equal("Uploading images...", vm.StatusText);
            Assert.False(vm.IsProgressVisible);

            // Step 2 — first progress report
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.25);
            await FlushDispatcherAsync();

            Assert.Equal(0.25, vm.Progress);
            Assert.True(vm.IsProgressVisible);

            // Step 3 — progress advances further
            ea.GetEvent<ProgressUpdateEvent>().Publish(0.75);
            await FlushDispatcherAsync();

            Assert.Equal(0.75, vm.Progress);
            Assert.True(vm.IsProgressVisible);

            // Step 4 — status updated while upload continues
            ea.GetEvent<StatusUpdateEvent>().Publish("Finalising...");
            await FlushDispatcherAsync();

            Assert.Equal("Finalising...", vm.StatusText);

            // Step 5 — upload finishes; progress bar must disappear and reset
            ea.GetEvent<UploadFinishedEvent>().Publish("Upload complete");
            await FlushDispatcherAsync();

            Assert.Equal(0.0, vm.Progress);
            Assert.False(vm.IsProgressVisible);
        }
    }
}
