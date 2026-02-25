using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Prism.Events;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.ViewModels
{
    public class ConfigPanelViewModel : ViewModelBase
    {
        private const int SaveDebounceMs = 500;

        private readonly DispatcherTimer _saveTimer;
        private readonly AppConfig _config;

        public List<string> StorageChoices { get; } = ["imgbb", "cyberdrop", "ipfs"];

        public string StorageChoice
        {
            get => _config.StorageChoice;
            set { _config.StorageChoice = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string TelegraphAccessToken
        {
            get => _config.TelegraphAccessToken;
            set { _config.TelegraphAccessToken = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string AuthorUrl
        {
            get => _config.AuthorUrl;
            set { _config.AuthorUrl = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string HeaderName
        {
            get => _config.HeaderName;
            set { _config.HeaderName = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string ImgbbApiKey
        {
            get => _config.ImgbbApiKey;
            set { _config.ImgbbApiKey = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string CyberdropToken
        {
            get => _config.CyberdropToken;
            set { _config.CyberdropToken = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string CyberdropAlbumId
        {
            get => _config.CyberdropAlbumId;
            set { _config.CyberdropAlbumId = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public int MaxWidth
        {
            get => _config.MaxWidth;
            set { _config.MaxWidth = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public int MaxHeight
        {
            get => _config.MaxHeight;
            set { _config.MaxHeight = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public int TotalDimensionThreshold
        {
            get => _config.TotalDimensionThreshold;
            set { _config.TotalDimensionThreshold = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public long MaxFileSize
        {
            get => _config.MaxFileSize;
            set { _config.MaxFileSize = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public int PauseSeconds
        {
            get => _config.PauseSeconds;
            set { _config.PauseSeconds = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public string OutputFolder
        {
            get => _config.OutputFolder;
            set { _config.OutputFolder = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public int DuplicateThreshold
        {
            get => _config.DuplicateThreshold;
            set { _config.DuplicateThreshold = value; RaisePropertyChanged(); ScheduleSave(); }
        }

        public ConfigPanelViewModel(IConfigService configService, IEventAggregator eventAggregator)
        {
            _config = configService.Load();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SaveDebounceMs) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                configService.Save(_config);
                eventAggregator.GetEvent<ConfigChangedEvent>().Publish(_config.Clone());
            };

            Initialize();
        }

        protected override void DefineCommands()
        {
        }

        protected override void DefineEvents()
        {
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }
}
