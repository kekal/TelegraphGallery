using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Shipwreck.Phash;
using Shipwreck.Phash.Bitmaps;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class DuplicateDetectionService : IDuplicateDetectionService
    {
        private const double PercentageDivisor = 100.0;

        private readonly ConcurrentDictionary<string, Digest> _digestCache = new();

        public void ClearCache() => _digestCache.Clear();

        public async Task<Dictionary<int, List<GalleryItem>>> FindDuplicatesAsync(
            IEnumerable<GalleryItem> items, int threshold,
            IProgress<(string Status, double Progress)>? progress = null)
        {
            return await Task.Run(() =>
            {
                var itemList = items.Where(i => !i.IsVideo).ToList();
                var totalImages = itemList.Count;
                var remaining = totalImages;

                // Phase 1: Compute digests (progress 0 → 1)
                var digests = new Dictionary<GalleryItem, Digest>();
                foreach (var item in itemList)
                {
                    try
                    {
                        if (_digestCache.TryGetValue(item.FilePath, out var cached))
                        {
                            digests[item] = cached;
                        }
                        else
                        {
                            using var bitmap = (Bitmap)Image.FromFile(item.FilePath);
                            var digest = ImagePhash.ComputeDigest(bitmap.ToLuminanceImage());
                            _digestCache[item.FilePath] = digest;
                            digests[item] = digest;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to compute pHash for {FilePath}", item.FilePath);
                    }

                    remaining--;
                    progress?.Report(($"Hashing {totalImages - remaining}/{totalImages}",
                        (double)(totalImages - remaining) / totalImages));
                }

                var hashItems = itemList.Where(i => digests.ContainsKey(i)).ToList();
                var visited = new HashSet<int>();
                var groups = new Dictionary<int, List<GalleryItem>>();
                var groupId = 0;

                // threshold is treated as max % difference (e.g. 5 → images must be ≥ 95% similar)
                var minCorrelation = 1.0 - threshold / PercentageDivisor;

                // Phase 2: Compare pairs (progress 0 → 1)
                var totalPairs = (long)hashItems.Count * (hashItems.Count - 1) / 2;
                long comparedPairs = 0;

                for (var i = 0; i < hashItems.Count; i++)
                {
                    if (visited.Contains(i))
                    {
                        comparedPairs += hashItems.Count - i - 1;
                        if (totalPairs > 0)
                            progress?.Report(($"Comparing {comparedPairs}/{totalPairs}",
                                (double)comparedPairs / totalPairs));
                        continue;
                    }

                    var group = new List<GalleryItem>();

                    for (var j = i + 1; j < hashItems.Count; j++)
                    {
                        comparedPairs++;

                        if (visited.Contains(j))
                        {
                            continue;
                        }

                        var correlation = ImagePhash.GetCrossCorrelation(
                            digests[hashItems[i]],
                            digests[hashItems[j]]);

                        if (correlation >= minCorrelation)
                        {
                            if (group.Count == 0)
                            {
                                group.Add(hashItems[i]);
                                visited.Add(i);
                            }
                            group.Add(hashItems[j]);
                            visited.Add(j);
                        }
                    }

                    if (group.Count > 0)
                    {
                        groups[groupId] = group;
                        groupId++;
                    }

                    if (totalPairs > 0)
                    {
                        progress?.Report(($"Comparing {comparedPairs}/{totalPairs}",
                            (double)comparedPairs / totalPairs));
                    }
                }

                return groups;
            }).ConfigureAwait(false);
        }
    }
}
