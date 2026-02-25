using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Events;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.ViewModels
{
    public class ToolbarViewModel : ViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IUploadCacheService _uploadCacheService;
        private readonly IThumbnailService _thumbnailService;
        private readonly IProcessLauncher _processLauncher;

        public List<string> SortModes { get; } = ["Name", "File Timestamp", "EXIF Date", "Custom"];

        private string _selectedSortMode;
        public string SelectedSortMode
        {
            get => _selectedSortMode;
            set
            {
                if (SetProperty(ref _selectedSortMode, value))
                {
                    _eventAggregator.GetEvent<SortChangedEvent>().Publish(value);
                }
            }
        }

        private int _thumbnailSize;
        public int ThumbnailSize
        {
            get => _thumbnailSize;
            set
            {
                if (SetProperty(ref _thumbnailSize, value))
                {
                    _eventAggregator.GetEvent<ThumbnailSizeChangedEvent>().Publish(value);
                }
            }
        }

        private bool _isUploading;
        public bool IsUploading
        {
            get => _isUploading;
            set => SetProperty(ref _isUploading, value);
        }

        private bool _isGalleryLoaded;
        public bool IsGalleryLoaded
        {
            get => _isGalleryLoaded;
            set => SetProperty(ref _isGalleryLoaded, value);
        }

        private bool _isSettingsValid;
        public bool IsSettingsValid
        {
            get => _isSettingsValid;
            set => SetProperty(ref _isSettingsValid, value);
        }

        private string? _lastResultUrl;
        public string? LastResultUrl
        {
            get => _lastResultUrl;
            set => SetProperty(ref _lastResultUrl, value);
        }

        public DelegateCommand OpenFolderCommand { get; private set; } = null!;
        public DelegateCommand UploadAllCommand { get; private set; } = null!;
        public DelegateCommand FindDuplicatesCommand { get; private set; } = null!;
        public DelegateCommand CancelUploadCommand { get; private set; } = null!;
        public DelegateCommand ToggleSettingsCommand { get; private set; } = null!;
        public DelegateCommand CopyResultUrlCommand { get; private set; } = null!;
        public DelegateCommand OpenResultUrlCommand { get; private set; } = null!;
        public DelegateCommand ClearCacheCommand { get; private set; } = null!;

        public ToolbarViewModel(IEventAggregator eventAggregator, IConfigService configService,
            IUploadCacheService uploadCacheService, IThumbnailService thumbnailService,
            IProcessLauncher processLauncher)
        {
            _eventAggregator = eventAggregator;
            _uploadCacheService = uploadCacheService;
            _thumbnailService = thumbnailService;
            _processLauncher = processLauncher;

            var config = configService.Load();
            _selectedSortMode = config.SortMode;
            _thumbnailSize = config.ThumbnailSize;
            _isSettingsValid = ValidateSettings(config);

            Initialize();
        }

        protected override void DefineCommands()
        {
            OpenFolderCommand = new DelegateCommand(OnOpenFolder);

            UploadAllCommand = new DelegateCommand(OnUploadAll, () => !IsUploading && IsSettingsValid && IsGalleryLoaded)
                .ObservesProperty(() => IsUploading)
                .ObservesProperty(() => IsSettingsValid)
                .ObservesProperty(() => IsGalleryLoaded);

            FindDuplicatesCommand = new DelegateCommand(OnFindDuplicates, () => !IsUploading && IsGalleryLoaded)
                .ObservesProperty(() => IsUploading)
                .ObservesProperty(() => IsGalleryLoaded);

            CancelUploadCommand = new DelegateCommand(OnCancelUpload, () => IsUploading)
                .ObservesProperty(() => IsUploading);

            ToggleSettingsCommand = new DelegateCommand(OnToggleSettings);

            CopyResultUrlCommand = new DelegateCommand(OnCopyResultUrl, () => !string.IsNullOrEmpty(LastResultUrl))
                .ObservesProperty(() => LastResultUrl);

            OpenResultUrlCommand = new DelegateCommand(OnOpenResultUrl, () => !string.IsNullOrEmpty(LastResultUrl))
                .ObservesProperty(() => LastResultUrl);

            ClearCacheCommand = new DelegateCommand(OnClearCache);
        }

        protected override void DefineEvents()
        {
            _eventAggregator.GetEvent<UploadStartedEvent>().Subscribe(() => IsUploading = true);
            _eventAggregator.GetEvent<UploadFinishedEvent>().Subscribe(_ => IsUploading = false);
            _eventAggregator.GetEvent<GalleryLoadingEvent>().Subscribe(() => IsGalleryLoaded = false);
            _eventAggregator.GetEvent<GalleryLoadedEvent>().Subscribe(_ => IsGalleryLoaded = true);

            _eventAggregator.GetEvent<ConfigChangedEvent>().Subscribe(cfg =>
            {
                IsSettingsValid = ValidateSettings(cfg);
            });

            _eventAggregator.GetEvent<PageCreatedEvent>().Subscribe(url =>
            {
                LastResultUrl = url;
            });

            _eventAggregator.GetEvent<SortChangedEvent>().Subscribe(sortMode =>
            {
                if (_selectedSortMode != sortMode)
                {
                    _selectedSortMode = sortMode;
                    RaisePropertyChanged(nameof(SelectedSortMode));
                }
            });
        }

        private static bool ValidateSettings(AppConfig config)
        {
            return config.StorageChoice switch
            {
                "imgbb" => !string.IsNullOrWhiteSpace(config.ImgbbApiKey),
                "cyberdrop" => !string.IsNullOrWhiteSpace(config.CyberdropToken),
                _ => true
            };
        }

        private void OnToggleSettings()
        {
            _eventAggregator.GetEvent<ToggleConfigPanelEvent>().Publish();
        }

        private void OnOpenFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select folder with images"
            };

            if (dialog.ShowDialog() == true)
            {
                _eventAggregator.GetEvent<OpenFolderEvent>().Publish(dialog.FolderName);
            }
        }

        private void OnUploadAll()
        {
            _eventAggregator.GetEvent<UploadAllEvent>().Publish();
        }

        private void OnFindDuplicates()
        {
            _eventAggregator.GetEvent<FindDuplicatesEvent>().Publish();
        }

        private void OnCancelUpload()
        {
            _eventAggregator.GetEvent<CancelUploadEvent>().Publish();
        }

        private void OnCopyResultUrl()
        {
            if (!string.IsNullOrEmpty(LastResultUrl))
            {
                Clipboard.SetText(LastResultUrl);
            }
        }

        private void OnClearCache()
        {
            _uploadCacheService.ClearAll();
            _thumbnailService.ClearCache();
            _eventAggregator.GetEvent<StatusUpdateEvent>().Publish("Cache cleared");
        }

        private void OnOpenResultUrl()
        {
            if (!string.IsNullOrEmpty(LastResultUrl))
            {
                _processLauncher.OpenUrl(LastResultUrl);
            }
        }
    }
}
