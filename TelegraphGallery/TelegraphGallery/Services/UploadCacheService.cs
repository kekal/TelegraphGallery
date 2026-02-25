using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class UploadCacheService : IUploadCacheService, IDisposable
    {
        private static readonly string CacheFilePath = AppPaths.UploadCacheFile;

        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

        private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(5);

        private ConcurrentDictionary<string, UploadCacheEntry> _cache = new();
        private bool _isDirty;
        private readonly object _lock = new();
        private readonly Timer _autoSaveTimer;

        public UploadCacheService()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
            LoadFromDisk();
            _autoSaveTimer = new Timer(_ => AutoSave(), null, AutoSaveInterval, AutoSaveInterval);
        }

        public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default)
        {
            return await FileHashHelper.ComputeXxHash128Async(filePath, ct).ConfigureAwait(false);
        }

        public UploadCacheEntry? TryGet(string contentHash, string storageProvider)
        {
            lock (_lock)
            {
                var key = $"{contentHash}:{storageProvider}";
                return _cache.GetValueOrDefault(key);
            }
        }

        public void Set(string contentHash, string storageProvider, UploadCacheEntry entry)
        {
            lock (_lock)
            {
                var key = $"{contentHash}:{storageProvider}";
                _cache[key] = entry;
                _isDirty = true;
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (_isDirty)
                {
                    SaveToDisk();
                    _isDirty = false;
                }
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _cache.Clear();
                _isDirty = false;
                try
                {
                    var path = EffectiveCacheFilePath;
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete upload cache file");
                }
            }
        }

        // Visible for testing via a new instance constructor pattern
        public UploadCacheService(string cacheFilePath)
        {
            CacheFilePathOverride = cacheFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
            LoadFromDisk();
            _autoSaveTimer = new Timer(_ => AutoSave(), null, AutoSaveInterval, AutoSaveInterval);
        }

        private string? CacheFilePathOverride { get; }

        private string EffectiveCacheFilePath => CacheFilePathOverride ?? CacheFilePath;

        private void LoadFromDisk()
        {
            var path = EffectiveCacheFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<Dictionary<string, UploadCacheEntry>>(json);
                if (entries != null)
                {
                    var cutoff = DateTime.UtcNow - MaxAge;
                    var valid = entries.Where(kv => kv.Value.CachedAtUtc >= cutoff);
                    _cache = new ConcurrentDictionary<string, UploadCacheEntry>(valid);

                    if (_cache.Count < entries.Count)
                    {
                        Log.Information("Pruned {Count} expired upload cache entries",
                            entries.Count - _cache.Count);
                        SaveToDisk();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load upload cache, starting fresh");
                _cache = new ConcurrentDictionary<string, UploadCacheEntry>();
            }
        }

        private void SaveToDisk()
        {
            try
            {
                var path = EffectiveCacheFilePath;
                var json = JsonSerializer.Serialize(
                    _cache.ToDictionary(kv => kv.Key, kv => kv.Value),
                    new JsonSerializerOptions { WriteIndented = true });
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save upload cache");
            }
        }

        private void AutoSave()
        {
            if (!Monitor.TryEnter(_lock))
            {
                return; // Another thread holds the lock (e.g. Flush); skip this tick.
            }

            try
            {
                if (_isDirty)
                {
                    SaveToDisk();
                    _isDirty = false;
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        public void Dispose()
        {
            _autoSaveTimer.Dispose();
            Flush();
        }
    }
}
