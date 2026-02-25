using System;
using System.Collections.Generic;
using Prism.Commands;
using Prism.Services.Dialogs;
using TelegraphGallery.Core.Mvvm;

namespace TelegraphGallery.Dialogs
{
    public class ErrorSummaryDialogViewModel : ViewModelBase, IDialogAware
    {
        private List<string> _errors = [];
        public List<string> Errors
        {
            get => _errors;
            set => SetProperty(ref _errors, value);
        }

        public string Title => "Upload Errors";

        public DelegateCommand CloseCommand { get; private set; } = null!;

        public event Action<IDialogResult>? RequestClose;

        public ErrorSummaryDialogViewModel()
        {
            Initialize();
        }

        protected override void DefineCommands()
        {
            CloseCommand = new DelegateCommand(() =>
                RequestClose?.Invoke(new DialogResult(ButtonResult.OK)));
        }

        protected override void DefineEvents()
        {
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Errors"))
            {
                Errors = parameters.GetValue<List<string>>("Errors");
            }
        }
    }
}
