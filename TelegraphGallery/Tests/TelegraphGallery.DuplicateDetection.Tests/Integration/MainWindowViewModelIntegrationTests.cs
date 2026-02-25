using System;
using System.Collections.Generic;
using System.ComponentModel;
using Moq;
using Prism.Events;
using Prism.Regions;
using TelegraphGallery.Events;
using TelegraphGallery.ViewModels;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    /// <summary>
    /// Integration tests for <see cref="MainWindowViewModel"/>.
    ///
    /// Strategy:
    ///   - IRegionManager / IRegion / IRegionCollection are mocked with Moq because they are
    ///     Prism UI-infrastructure objects that cannot be instantiated without a full Prism shell.
    ///   - IEventAggregator uses a real <see cref="EventAggregator"/> instance so that the
    ///     pub/sub wiring exercised by <see cref="ToggleConfigPanelEvent"/> is tested end-to-end.
    /// </summary>
    [Collection("Integration")]
    public class MainWindowViewModelIntegrationTests : IDisposable
    {
        // ── Fields ────────────────────────────────────────────────────────────

        private readonly IEventAggregator _eventAggregator;

        // ── Constructor ───────────────────────────────────────────────────────

        public MainWindowViewModelIntegrationTests(IntegrationTestFixture _)
        {
            _eventAggregator = IntegrationTestFixture.CreateEventAggregator();
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            // Nothing to release; EventAggregator has no unmanaged state.
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a fully-configured mock region set where "ConfigRegion" contains
        /// exactly <paramref name="view"/> and tracks activation state in-process.
        /// </summary>
        private static (Mock<IRegionManager> MockRegionManager,
                        Mock<IRegion>         MockRegion,
                        object                View,
                        List<object>          ActiveViews)
            BuildRegionMocks()
        {
            var view        = new object();
            var viewsList   = new List<object> { view };
            var activeViews = new List<object>();

            var mockViews = new Mock<IViewsCollection>();
            mockViews.Setup(v => v.GetEnumerator()).Returns(() => viewsList.GetEnumerator());

            var mockActiveViews = new Mock<IViewsCollection>();
            mockActiveViews.Setup(v => v.GetEnumerator()).Returns(() => activeViews.GetEnumerator());
            mockActiveViews.Setup(v => v.Contains(It.IsAny<object>()))
                .Returns<object>(o => activeViews.Contains(o));

            var mockRegion = new Mock<IRegion>();
            mockRegion.Setup(r => r.Views).Returns(mockViews.Object);
            mockRegion.Setup(r => r.ActiveViews).Returns(mockActiveViews.Object);
            mockRegion
                .Setup(r => r.Activate(It.IsAny<object>()))
                .Callback<object>(v => activeViews.Add(v));
            mockRegion
                .Setup(r => r.Deactivate(It.IsAny<object>()))
                .Callback<object>(v => activeViews.Remove(v));

            var mockRegionCollection = new Mock<IRegionCollection>();
            mockRegionCollection
                .Setup(rc => rc["ConfigRegion"])
                .Returns(mockRegion.Object);

            var mockRegionManager = new Mock<IRegionManager>();
            mockRegionManager
                .Setup(rm => rm.Regions)
                .Returns(mockRegionCollection.Object);

            return (mockRegionManager, mockRegion, view, activeViews);
        }

        /// <summary>
        /// Creates a <see cref="MainWindowViewModel"/> wired to the supplied mock region manager
        /// and the shared real event aggregator.
        /// </summary>
        private MainWindowViewModel CreateViewModel(Mock<IRegionManager> mockRegionManager)
            => new(mockRegionManager.Object, _eventAggregator);

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_SetsDefaultTitle()
        {
            // Arrange
            var (mockRegionManager, _, _, _) = BuildRegionMocks();

            // Act
            var vm = CreateViewModel(mockRegionManager);

            // Assert
            Assert.Equal("Telegraph Gallery", vm.Title);
        }

        [Fact]
        public void Constructor_IsConfigVisibleDefaultsFalse()
        {
            // Arrange
            var (mockRegionManager, _, _, _) = BuildRegionMocks();

            // Act
            var vm = CreateViewModel(mockRegionManager);

            // Assert
            Assert.False(vm.IsConfigVisible);
        }

        [Fact]
        public void ToggleConfigPanelCommand_IsInitialized()
        {
            // Arrange
            var (mockRegionManager, _, _, _) = BuildRegionMocks();

            // Act
            var vm = CreateViewModel(mockRegionManager);

            // Assert — command must not be null after Initialize()
            Assert.NotNull(vm.ToggleConfigPanelCommand);
        }

        [Fact]
        public void ToggleConfigPanel_ActivatesRegionView()
        {
            // Arrange
            var (mockRegionManager, mockRegion, view, activeViews) = BuildRegionMocks();
            var vm = CreateViewModel(mockRegionManager);

            // Act — panel starts inactive; one execution should activate
            vm.ToggleConfigPanelCommand.Execute();

            // Assert — Activate must have been called with the registered view
            mockRegion.Verify(r => r.Activate(view), Times.Once);
            Assert.True(vm.IsConfigVisible);
        }

        [Fact]
        public void ToggleConfigPanel_DeactivatesWhenActive()
        {
            // Arrange — pre-populate the active views list so the panel appears already open
            var (mockRegionManager, mockRegion, view, activeViews) = BuildRegionMocks();
            activeViews.Add(view); // panel is already active

            var vm = CreateViewModel(mockRegionManager);

            // Act — executing with an active view should deactivate it
            vm.ToggleConfigPanelCommand.Execute();

            // Assert — Deactivate must have been called and the flag reset
            mockRegion.Verify(r => r.Deactivate(view), Times.Once);
            Assert.False(vm.IsConfigVisible);
        }

        [Fact]
        public void ToggleConfigPanel_TogglesBackAndForth()
        {
            // Arrange
            var (mockRegionManager, mockRegion, view, activeViews) = BuildRegionMocks();
            var vm = CreateViewModel(mockRegionManager);

            // Act — toggle 1: open
            vm.ToggleConfigPanelCommand.Execute();
            Assert.True(vm.IsConfigVisible);
            mockRegion.Verify(r => r.Activate(view), Times.Once);

            // Act — toggle 2: close
            vm.ToggleConfigPanelCommand.Execute();
            Assert.False(vm.IsConfigVisible);
            mockRegion.Verify(r => r.Deactivate(view), Times.Once);

            // Act — toggle 3: open again
            vm.ToggleConfigPanelCommand.Execute();
            Assert.True(vm.IsConfigVisible);
            mockRegion.Verify(r => r.Activate(view), Times.Exactly(2));
        }

        [Fact]
        public void ToggleConfigPanelEvent_TriggersToggle()
        {
            // Arrange
            var (mockRegionManager, mockRegion, view, _) = BuildRegionMocks();
            var vm = CreateViewModel(mockRegionManager);

            Assert.False(vm.IsConfigVisible);

            // Act — publish the event (the VM subscribed via real EventAggregator in DefineEvents)
            _eventAggregator.GetEvent<ToggleConfigPanelEvent>().Publish();

            // Assert — the panel must now be visible and the region activated
            Assert.True(vm.IsConfigVisible);
            mockRegion.Verify(r => r.Activate(view), Times.Once);
        }

        [Fact]
        public void Title_CanBeChanged()
        {
            // Arrange
            var (mockRegionManager, _, _, _) = BuildRegionMocks();
            var vm = CreateViewModel(mockRegionManager);

            var raisedProperties = new List<string?>();
            vm.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

            // Act
            vm.Title = "Custom Title";

            // Assert — backing value updated and PropertyChanged raised
            Assert.Equal("Custom Title", vm.Title);
            Assert.Contains(nameof(vm.Title), raisedProperties);
        }
    }
}
