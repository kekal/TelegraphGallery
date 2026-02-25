using System.Linq;
using System.Windows;
using DryIoc;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;
using Serilog;
using TelegraphGallery.Dialogs;
using TelegraphGallery.Services;
using TelegraphGallery.Services.Interfaces;
using TelegraphGallery.Services.Upload;
using TelegraphGallery.Views;

namespace TelegraphGallery
{
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day)
                .WriteTo.Debug()
                .CreateLogger();

            AppPaths.EnsureDirectoriesExist();

            base.OnStartup(e);
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // Services (singletons)
            containerRegistry.RegisterSingleton<IConfigService, ConfigService>();
            containerRegistry.RegisterSingleton<IThumbnailService, ThumbnailService>();
            containerRegistry.RegisterSingleton<IImageProcessingService, ImageProcessingService>();
            containerRegistry.RegisterSingleton<IDuplicateDetectionService, DuplicateDetectionService>();
            containerRegistry.RegisterSingleton<ITelegraphService, TelegraphService>();
            containerRegistry.RegisterSingleton<IUploadServiceFactory, UploadServiceFactory>();
            containerRegistry.RegisterSingleton<IUploadCacheService, UploadCacheService>();
            containerRegistry.RegisterSingleton<IProcessLauncher, ProcessLauncher>();

            // Upload services (keyed registrations via DryIoc native API)
            var container = containerRegistry.GetContainer();
            container.Register<IUploadService, ImgbbUploadService>(serviceKey: "imgbb");
            container.Register<IUploadService, CyberdropUploadService>(serviceKey: "cyberdrop");
            container.Register<IUploadService, IpfsUploadService>(serviceKey: "ipfs");

            // Dialogs
            containerRegistry.RegisterDialog<ErrorSummaryDialog, ErrorSummaryDialogViewModel>("ErrorSummaryDialog");
            containerRegistry.RegisterDialog<UploadSuccessDialog, UploadSuccessDialogViewModel>("UploadSuccessDialog");

            // Views for regions
            containerRegistry.RegisterForNavigation<GalleryView>();
            containerRegistry.RegisterForNavigation<ConfigPanelView>();
            containerRegistry.RegisterForNavigation<ToolbarView>();
            containerRegistry.RegisterForNavigation<StatusBarView>();
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            var regionManager = Container.Resolve<IRegionManager>();
            regionManager.RequestNavigate("GalleryRegion", nameof(GalleryView));
            regionManager.RequestNavigate("ConfigRegion", nameof(ConfigPanelView));
            regionManager.RequestNavigate("ToolbarRegion", nameof(ToolbarView));
            regionManager.RequestNavigate("StatusBarRegion", nameof(StatusBarView));

            // Hide config panel by default
            var configRegion = regionManager.Regions["ConfigRegion"];
            var configView = configRegion.Views.FirstOrDefault();
            if (configView != null)
            {
                configRegion.Deactivate(configView);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
