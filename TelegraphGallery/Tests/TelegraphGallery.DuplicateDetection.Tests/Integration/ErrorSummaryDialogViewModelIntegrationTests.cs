using System;
using System.Collections.Generic;
using System.ComponentModel;
using Prism.Services.Dialogs;
using TelegraphGallery.Dialogs;
using Xunit;

namespace TelegraphGallery.Tests.Integration
{
    /// <summary>
    /// Integration tests for <see cref="ErrorSummaryDialogViewModel"/>.
    ///
    /// <see cref="ErrorSummaryDialogViewModel"/> has a parameterless constructor and no
    /// external dependencies, so every test is fully self-contained — no mocks required.
    /// </summary>
    [Collection("Integration")]
    public class ErrorSummaryDialogViewModelIntegrationTests : IDisposable
    {
        // ── Constructor ───────────────────────────────────────────────────────

        public ErrorSummaryDialogViewModelIntegrationTests(IntegrationTestFixture _)
        {
            // No shared state to set up; each test creates its own ViewModel instance.
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            // Nothing to release.
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Creates a fresh <see cref="ErrorSummaryDialogViewModel"/> for each test.</summary>
        private static ErrorSummaryDialogViewModel CreateViewModel() => new();

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_InitializesCloseCommand()
        {
            // Act
            var vm = CreateViewModel();

            // Assert — CloseCommand must be set by DefineCommands() during Initialize()
            Assert.NotNull(vm.CloseCommand);
        }

        [Fact]
        public void Title_ReturnsUploadErrors()
        {
            // Act
            var vm = CreateViewModel();

            // Assert
            Assert.Equal("Upload Errors", vm.Title);
        }

        [Fact]
        public void Errors_DefaultsToEmptyList()
        {
            // Act
            var vm = CreateViewModel();

            // Assert — the backing field is initialised to an empty list literal
            Assert.NotNull(vm.Errors);
            Assert.Empty(vm.Errors);
        }

        [Fact]
        public void OnDialogOpened_SetsErrors()
        {
            // Arrange
            var vm         = CreateViewModel();
            var errorList  = new List<string> { "Error A", "Error B", "Error C" };
            var parameters = new DialogParameters { { "Errors", errorList } };

            // Act
            vm.OnDialogOpened(parameters);

            // Assert
            Assert.Equal(errorList, vm.Errors);
        }

        [Fact]
        public void OnDialogOpened_WithoutErrors_KeepsDefault()
        {
            // Arrange — parameters do not contain the "Errors" key
            var vm         = CreateViewModel();
            var parameters = new DialogParameters();

            // Act
            vm.OnDialogOpened(parameters);

            // Assert — Errors must remain the default empty list
            Assert.NotNull(vm.Errors);
            Assert.Empty(vm.Errors);
        }

        [Fact]
        public void CloseCommand_InvokesRequestClose()
        {
            // Arrange
            var vm = CreateViewModel();

            IDialogResult? capturedResult = null;
            vm.RequestClose += result => capturedResult = result;

            // Act
            vm.CloseCommand.Execute();

            // Assert — RequestClose must have been raised with ButtonResult.OK
            Assert.NotNull(capturedResult);
            Assert.Equal(ButtonResult.OK, capturedResult!.Result);
        }

        [Fact]
        public void CanCloseDialog_ReturnsTrue()
        {
            // Act
            var vm     = CreateViewModel();
            var result = vm.CanCloseDialog();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void OnDialogClosed_DoesNotThrow()
        {
            // Arrange
            var vm = CreateViewModel();

            // Act & Assert — must not throw
            var ex = Record.Exception(() => vm.OnDialogClosed());
            Assert.Null(ex);
        }

        [Fact]
        public void Errors_PropertyChanged_FiresNotification()
        {
            // Arrange
            var vm                  = CreateViewModel();
            var raisedPropertyNames = new List<string?>();
            vm.PropertyChanged += (_, e) => raisedPropertyNames.Add(e.PropertyName);

            // Act
            vm.Errors = new List<string> { "New Error" };

            // Assert — BindableBase.SetProperty must have raised PropertyChanged for "Errors"
            Assert.Contains(nameof(vm.Errors), raisedPropertyNames);
        }

        [Fact]
        public void OnDialogOpened_MultipleErrors_AllPresent()
        {
            // Arrange
            var vm = CreateViewModel();
            var errorList = new List<string>
            {
                "Upload failed: timeout",
                "Image too large",
                "Invalid API key",
                "Network unreachable",
                "Server returned 500"
            };
            var parameters = new DialogParameters { { "Errors", errorList } };

            // Act
            vm.OnDialogOpened(parameters);

            // Assert — all five entries must be present, order preserved
            Assert.Equal(5, vm.Errors.Count);
            for (var i = 0; i < errorList.Count; i++)
            {
                Assert.Equal(errorList[i], vm.Errors[i]);
            }
        }
    }
}
