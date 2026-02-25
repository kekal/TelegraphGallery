using System;
using System.Windows.Media.Imaging;
using Prism.Mvvm;

namespace TelegraphGallery.Models
{
    public class GalleryItem : BindableBase
    {
        public string FilePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public string SubFolder { get; init; } = "";
        public DateTime FileTimestamp { get; init; }
        public DateTime? ExifTimestamp { get; init; }
        public bool IsVideo { get; init; }

        private bool _isExcluded;
        public bool IsExcluded
        {
            get => _isExcluded;
            set => SetProperty(ref _isExcluded, value);
        }

        private bool _isLoadingThumbnail;
        public bool IsLoadingThumbnail
        {
            get => _isLoadingThumbnail;
            set => SetProperty(ref _isLoadingThumbnail, value);
        }

        private int? _duplicateGroupId;
        public int? DuplicateGroupId
        {
            get => _duplicateGroupId;
            set => SetProperty(ref _duplicateGroupId, value);
        }

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }
    }
}
