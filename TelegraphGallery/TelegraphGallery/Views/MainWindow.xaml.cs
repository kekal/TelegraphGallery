using System.IO;
using System.Linq;
using System.Windows;
using Prism.Events;
using TelegraphGallery.Events;

namespace TelegraphGallery.Views
{
    public partial class MainWindow
    {
        private readonly IEventAggregator _eventAggregator;

        public MainWindow(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            InitializeComponent();
        }

        private void MainWindow_OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Any(Directory.Exists))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                var folder = files.FirstOrDefault(Directory.Exists);
                if (folder != null)
                {
                    _eventAggregator.GetEvent<OpenFolderEvent>().Publish(folder);
                }
            }
        }
    }
}
