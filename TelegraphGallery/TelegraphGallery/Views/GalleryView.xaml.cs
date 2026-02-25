using Serilog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TelegraphGallery.Models;

namespace TelegraphGallery.Views
{
    public partial class GalleryView
    {
        public GalleryView()
        {
            InitializeComponent();
        }

        private void Thumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: GalleryItem item })
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to open file {File}", item.FilePath);
                }
            }
        }
    }
}
