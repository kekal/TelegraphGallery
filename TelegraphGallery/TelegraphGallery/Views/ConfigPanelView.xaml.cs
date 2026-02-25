using System.Windows;
using TelegraphGallery.ViewModels;

namespace TelegraphGallery.Views
{
    public partial class ConfigPanelView
    {
        public ConfigPanelView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ConfigPanelViewModel vm)
                {
                    ImgbbPasswordBox.Password = vm.ImgbbApiKey;
                    CyberdropPasswordBox.Password = vm.CyberdropToken;
                }
            };
        }

        private void ImgbbPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ConfigPanelViewModel vm)
            {
                vm.ImgbbApiKey = ImgbbPasswordBox.Password;
            }
        }

        private void CyberdropPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ConfigPanelViewModel vm)
            {
                vm.CyberdropToken = CyberdropPasswordBox.Password;
            }
        }
    }
}
