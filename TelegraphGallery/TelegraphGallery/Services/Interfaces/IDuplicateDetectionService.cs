using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegraphGallery.Models;

namespace TelegraphGallery.Services.Interfaces
{
    public interface IDuplicateDetectionService
    {
        Task<Dictionary<int, List<GalleryItem>>> FindDuplicatesAsync(
            IEnumerable<GalleryItem> items, int threshold,
            IProgress<(string Status, double Progress)>? progress = null);

        void ClearCache();
    }
}
