using Prism.Events;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Events;

namespace TelegraphGallery.ViewModels
{
    public class StatusBarViewModel : ViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private bool _isProgressVisible;
        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        public StatusBarViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;

            Initialize();
        }

        protected override void DefineCommands()
        {
        }

        protected override void DefineEvents()
        {
            _eventAggregator.GetEvent<StatusUpdateEvent>().Subscribe(msg =>
            {
                StatusText = msg;
            }, ThreadOption.UIThread);

            _eventAggregator.GetEvent<UploadFinishedEvent>().Subscribe(_ =>
            {
                IsProgressVisible = false;
                Progress = 0;
            }, ThreadOption.UIThread);

            _eventAggregator.GetEvent<GalleryLoadedEvent>().Subscribe(_ =>
            {
                IsProgressVisible = false;
                Progress = 0;
            }, ThreadOption.UIThread);

            _eventAggregator.GetEvent<ProgressUpdateEvent>().Subscribe(p =>
            {
                Progress = p;
                IsProgressVisible = p > 0;
            }, ThreadOption.UIThread);
        }
    }
}
