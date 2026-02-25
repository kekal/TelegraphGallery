using System;
using System.Windows;
using Prism.Commands;
using Prism.Services.Dialogs;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Dialogs
{
    public class UploadSuccessDialogViewModel : ViewModelBase, IDialogAware
    {
        private readonly IProcessLauncher _processLauncher;

        private string _url = string.Empty;
        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string Title => "Upload Complete";

        public DelegateCommand CloseCommand { get; private set; } = null!;
        public DelegateCommand CopyUrlCommand { get; private set; } = null!;
        public DelegateCommand OpenUrlCommand { get; private set; } = null!;

        public event Action<IDialogResult>? RequestClose;

        public UploadSuccessDialogViewModel(IProcessLauncher processLauncher)
        {
            _processLauncher = processLauncher;
            Initialize();
        }

        protected override void DefineCommands()
        {
            CloseCommand = new DelegateCommand(() =>
                RequestClose?.Invoke(new DialogResult(ButtonResult.OK)));

            CopyUrlCommand = new DelegateCommand(() =>
            {
                if (!string.IsNullOrEmpty(Url))
                {
                    Clipboard.SetText(Url);
                }
            });

            OpenUrlCommand = new DelegateCommand(() =>
            {
                if (!string.IsNullOrEmpty(Url))
                {
                    _processLauncher.OpenUrl(Url);
                }
            });
        }

        protected override void DefineEvents()
        {
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.ContainsKey("Url"))
            {
                Url = parameters.GetValue<string>("Url");
            }
        }
    }
}
