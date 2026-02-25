using System.Linq;
using Prism.Commands;
using Prism.Events;
using Prism.Regions;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Events;

namespace TelegraphGallery.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        private string _title = "Telegraph Gallery";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private bool _isConfigVisible;
        public bool IsConfigVisible
        {
            get => _isConfigVisible;
            set => SetProperty(ref _isConfigVisible, value);
        }

        public DelegateCommand ToggleConfigPanelCommand { get; private set; } = null!;

        public MainWindowViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;

            Initialize();
        }

        protected override void DefineCommands()
        {
            ToggleConfigPanelCommand = new DelegateCommand(ToggleConfigPanel);
        }

        protected override void DefineEvents()
        {
            _eventAggregator.GetEvent<ToggleConfigPanelEvent>().Subscribe(ToggleConfigPanel);
        }

        private void ToggleConfigPanel()
        {
            var region = _regionManager.Regions["ConfigRegion"];
            if (region.Views.FirstOrDefault() is { } view)
            {
                if (region.ActiveViews.Contains(view))
                {
                    region.Deactivate(view);
                    IsConfigVisible = false;
                }
                else
                {
                    region.Activate(view);
                    IsConfigVisible = true;
                }
            }
        }
    }
}
