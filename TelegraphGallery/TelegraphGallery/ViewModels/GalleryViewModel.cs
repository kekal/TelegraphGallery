using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using NaturalSort.Extension;
using Prism.Commands;
using Prism.Events;
using Prism.Services.Dialogs;
using Serilog;
using TelegraphGallery.Core.Mvvm;
using TelegraphGallery.Events;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;
using Directory = System.IO.Directory;

namespace TelegraphGallery.ViewModels
{
    public class GalleryViewModel : ViewModelBase, IDropTarget
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".heic", ".heif" };
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm" };
        private static readonly HashSet<string> AllExtensions = new(
            ImageExtensions.Concat(VideoExtensions), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
            { "temp", ".idea", "old", ".git" };

        private readonly IThumbnailService _thumbnailService;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly IDuplicateDetectionService _duplicateDetectionService;
        private readonly IUploadServiceFactory _uploadServiceFactory;
        private readonly ITelegraphService _telegraphService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDialogService _dialogService;
        private readonly IUploadCacheService _uploadCacheService;

        private CancellationTokenSource? _uploadCts;
        private CancellationTokenSource? _thumbnailCts;
        private bool _isUploading;

        private string _currentSortMode;

        private int _thumbnailDisplaySize;
        public int ThumbnailDisplaySize
        {
            get => _thumbnailDisplaySize;
            private set => SetProperty(ref _thumbnailDisplaySize, value);
        }

        public ObservableCollection<GalleryItem> Items { get; } = [];

        private string? _currentFolderPath;
        public string? CurrentFolderPath
        {
            get => _currentFolderPath;
            set => SetProperty(ref _currentFolderPath, value);
        }

        public DelegateCommand<GalleryItem> ToggleExcludeCommand { get; private set; } = null!;

        public GalleryViewModel(
            IThumbnailService thumbnailService,
            IImageProcessingService imageProcessingService,
            IDuplicateDetectionService duplicateDetectionService,
            IUploadServiceFactory uploadServiceFactory,
            ITelegraphService telegraphService,
            IConfigService configService,
            IEventAggregator eventAggregator,
            IDialogService dialogService,
            IUploadCacheService uploadCacheService)
        {
            _thumbnailService = thumbnailService;
            _imageProcessingService = imageProcessingService;
            _duplicateDetectionService = duplicateDetectionService;
            _uploadServiceFactory = uploadServiceFactory;
            _telegraphService = telegraphService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _uploadCacheService = uploadCacheService;
            _dialogService = dialogService;

            var config = _configService.Load();
            _thumbnailDisplaySize = config.ThumbnailSize;
            _currentSortMode = config.SortMode;

            Initialize();
        }

        protected override void DefineCommands()
        {
            ToggleExcludeCommand = new DelegateCommand<GalleryItem>(item =>
            {
                if (item != null)
                {
                    item.IsExcluded = !item.IsExcluded;
                }
            });
        }

        protected override void DefineEvents()
        {
            _eventAggregator.GetEvent<OpenFolderEvent>().Subscribe(OnOpenFolder, keepSubscriberReferenceAlive: true);
            _eventAggregator.GetEvent<UploadAllEvent>().Subscribe(OnUploadAll, keepSubscriberReferenceAlive: true);
            _eventAggregator.GetEvent<FindDuplicatesEvent>().Subscribe(OnFindDuplicates, keepSubscriberReferenceAlive: true);
            _eventAggregator.GetEvent<SortChangedEvent>().Subscribe(OnSortChanged, keepSubscriberReferenceAlive: true);
            _eventAggregator.GetEvent<ThumbnailSizeChangedEvent>().Subscribe(size => ThumbnailDisplaySize = size, keepSubscriberReferenceAlive: true);
            _eventAggregator.GetEvent<CancelUploadEvent>().Subscribe(OnCancelUpload, keepSubscriberReferenceAlive: true);
        }

        private async void OnOpenFolder(string folderPath)
        {
            try
            {
                CurrentFolderPath = folderPath;
                Items.Clear();

                _eventAggregator.GetEvent<GalleryLoadingEvent>().Publish();
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish("Scanning folder...");

                var files = await Task.Run(() => ScanFolder(folderPath));

                foreach (var item in files)
                {
                    Items.Add(item);
                }

                ApplySort(_currentSortMode);

                _eventAggregator.GetEvent<GalleryLoadedEvent>().Publish(Items.Count);
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish($"Loaded {Items.Count} files");

                // Generate thumbnails — swap-then-dispose to avoid race conditions
                var oldCts = _thumbnailCts;
                _thumbnailCts = new CancellationTokenSource();
                if (oldCts != null) { await oldCts.CancelAsync(); oldCts.Dispose(); }

                await GenerateThumbnailsAsync(_thumbnailCts.Token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open folder");
            }
        }

        private List<GalleryItem> ScanFolder(string rootPath)
        {
            var items = new List<GalleryItem>();
            var rootDir = new DirectoryInfo(rootPath);

            ScanDirectory(rootDir, rootDir.FullName, items);
            return items;
        }

        private void ScanDirectory(DirectoryInfo dir, string rootPath, List<GalleryItem> items)
        {
            if (ExcludedFolders.Contains(dir.Name))
            {
                return;
            }

            try
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    if (!AllExtensions.Contains(file.Extension))
                    {
                        continue;
                    }

                    var subFolder = GetRelativeSubFolder(file.DirectoryName!, rootPath);
                    DateTime? exifDate = null;

                    if (ImageExtensions.Contains(file.Extension))
                    {
                        try
                        {
                            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
                            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                            if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                            {
                                exifDate = dt;
                            }
                        }
                        catch { /* skip EXIF errors */ }
                    }

                    items.Add(new GalleryItem
                    {
                        FilePath = file.FullName,
                        FileName = file.Name,
                        SubFolder = subFolder,
                        FileTimestamp = file.LastWriteTime,
                        ExifTimestamp = exifDate,
                        IsVideo = VideoExtensions.Contains(file.Extension)
                    });
                }

                foreach (var subDir in dir.EnumerateDirectories())
                {
                    ScanDirectory(subDir, rootPath, items);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error scanning directory {Dir}", dir.FullName);
            }
        }

        private static string GetRelativeSubFolder(string dirPath, string rootPath)
        {
            if (dirPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return Path.GetRelativePath(rootPath, dirPath);
        }

        private async Task GenerateThumbnailsAsync(CancellationToken ct = default)
        {
            var allItems = Items.ToList();
            var totalCount = allItems.Count;
            var processedCount = 0;

            if (totalCount == 0)
            {
                return;
            }

            // Mark all items as loading
            foreach (var item in allItems)
            {
                item.IsLoadingThumbnail = true;
            }

            _eventAggregator.GetEvent<StatusUpdateEvent>().Publish($"Loading thumbnails 0/{totalCount}...");
            _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(0);

            try
            {
                await Parallel.ForEachAsync(allItems,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = ct
                    },
                    async (item, token) =>
                    {
                        try
                        {
                            var thumb = await _thumbnailService.GenerateThumbnailAsync(item.FilePath, token);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                item.Thumbnail = thumb;
                                item.IsLoadingThumbnail = false;
                            });
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to generate thumbnail for {File}", item.FileName);
                            await Application.Current.Dispatcher.InvokeAsync(() => item.IsLoadingThumbnail = false);
                        }
                        finally
                        {
                            var done = Interlocked.Increment(ref processedCount);
                            if (!token.IsCancellationRequested)
                            {
                                var progress = (double)done / totalCount;
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    _eventAggregator.GetEvent<StatusUpdateEvent>()
                                        .Publish($"Loading thumbnails {done}/{totalCount}...");
                                    _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(progress);
                                });
                            }
                        }
                    });
            }
            catch (OperationCanceledException) { }

            // Clear loading state for any remaining items
            foreach (var item in allItems)
                item.IsLoadingThumbnail = false;

            if (!ct.IsCancellationRequested)
            {
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish($"Loaded {totalCount} thumbnails");
                _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(0);
            }
        }

        private void ApplySort(string sortMode)
        {
            if (sortMode == "Custom")
                return;

            IEnumerable<GalleryItem> sorted = sortMode switch
            {
                "File Timestamp" => Items.OrderBy(i => i.SubFolder).ThenBy(i => i.FileTimestamp),
                "EXIF Date" => Items.OrderBy(i => i.SubFolder).ThenBy(i => i.ExifTimestamp ?? i.FileTimestamp),
                _ => Items.OrderBy(i => i.SubFolder).ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase.WithNaturalSort())
            };

            var sortedList = sorted.ToList();
            Items.Clear();
            foreach (var item in sortedList)
            {
                Items.Add(item);
            }
        }

        private void SetSortMode(string sortMode, bool notify = true)
        {
            _currentSortMode = sortMode;
            if (notify)
            {
                _eventAggregator.GetEvent<SortChangedEvent>().Publish(sortMode);
            }
        }

        private void OnSortChanged(string sortMode)
        {
            SetSortMode(sortMode, notify: false);
            ApplySort(sortMode);
        }

        private async void OnFindDuplicates()
        {
            try
            {
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish("Finding duplicates...");
                _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(0.001);

                // Clear old duplicate groups
                foreach (var item in Items)
                    item.DuplicateGroupId = null;

                var config = _configService.Load();
                var progress = new Progress<(string Status, double Progress)>(update =>
                {
                    _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(update.Status);
                    _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(update.Progress);
                });
                var groups = await _duplicateDetectionService.FindDuplicatesAsync(
                    Items, config.DuplicateThreshold, progress);

                // Apply group IDs on the UI thread
                foreach (var (groupId, groupItems) in groups)
                {
                    foreach (var item in groupItems)
                        item.DuplicateGroupId = groupId;
                }

                // Reorder items to group duplicates together
                if (groups.Count > 0)
                {
                    var duplicateItems = Items.Where(i => i.DuplicateGroupId != null)
                        .OrderBy(i => i.DuplicateGroupId)
                        .ThenBy(i => i.FileName)
                        .ToList();

                    // Move duplicate items to the front, grouped together
                    var insertIndex = 0;
                    foreach (var item in duplicateItems)
                    {
                        var currentIndex = Items.IndexOf(item);
                        if (currentIndex != insertIndex)
                        {
                            Items.Move(currentIndex, insertIndex);
                        }

                        insertIndex++;
                    }

                    SetSortMode("Custom");
                }

                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(
                    groups.Count > 0 ? $"Found {groups.Count} duplicate groups" : "No duplicates found");
                _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Find duplicates failed");
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish($"Find duplicates failed: {ex.Message}");
                _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(0);
            }
        }

        private async void OnUploadAll()
        {
            if (_isUploading) return;
            _isUploading = true;

            _uploadCts?.Dispose();
            _uploadCts = new CancellationTokenSource();
            var ct = _uploadCts.Token;
            var config = _configService.Load();
            var errors = new List<string>();

            _eventAggregator.GetEvent<UploadStartedEvent>().Publish();
            var message = "Upload failed";

            try
            {
                var uploadService = _uploadServiceFactory.Create(config.StorageChoice);
                var activeItems = Items.Where(i => !i.IsExcluded).ToList();
                var groups = activeItems.GroupBy(i => i.SubFolder).ToList();

                var totalItems = activeItems.Count;
                var processedItems = 0;

                foreach (var group in groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var uploadResults = new List<UploadResult>();

                    foreach (var item in group)
                    {
                        ct.ThrowIfCancellationRequested();

                        var itemNum = processedItems + 1;
                        void PublishStep(string step) =>
                            _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(
                                $"Uploading {itemNum}/{totalItems}: {item.FileName} — {step}");

                        try
                        {
                            if (item.IsVideo)
                            {
                                PublishStep("Uploading video...");
                                var result = await uploadService.UploadFileAsync(item.FilePath, config, ct);
                                uploadResults.Add(result);
                                if (!result.Success)
                                {
                                    errors.Add($"Upload failed: {item.FileName} - {result.Error}");
                                }
                                else
                                {
                                    MoveToOutput(item, config, CurrentFolderPath!);
                                }
                            }
                            else
                            {
                                // Check upload cache
                                PublishStep("Hashing...");
                                var contentHash = await _uploadCacheService.ComputeFileHashAsync(
                                    item.FilePath, ct);
                                var cached = _uploadCacheService.TryGet(
                                    contentHash, config.StorageChoice);

                                if (cached != null)
                                {
                                    PublishStep("Cached, skipping upload");
                                    Log.Information("Cache hit for {File}, skipping upload",
                                        item.FileName);
                                    uploadResults.Add(new UploadResult(
                                        true, cached.DirectUrl, MediumUrl: cached.MediumUrl));
                                    MoveToOutput(item, config, CurrentFolderPath!);
                                }
                                else
                                {
                                    PublishStep("Processing image...");
                                    var processedPath = await _imageProcessingService
                                        .PrepareForUploadAsync(item.FilePath, config);

                                    if (processedPath == null)
                                    {
                                        errors.Add($"Failed to process: {item.FileName}");
                                        processedItems++;
                                        continue;
                                    }

                                    PublishStep("Generating medium size...");
                                    var mediumPath = await _imageProcessingService
                                        .PrepareMediumAsync(item.FilePath);

                                    // Upload full image
                                    PublishStep("Uploading full size...");
                                    var result = await uploadService.UploadFileAsync(
                                        processedPath, config, ct);

                                    // Upload medium image if available
                                    string? mediumUrl = null;
                                    if (mediumPath != null)
                                    {
                                        PublishStep("Uploading medium size...");
                                        var mediumResult = await uploadService.UploadFileAsync(
                                            mediumPath, config, ct);
                                        if (mediumResult.Success)
                                        {
                                            mediumUrl = mediumResult.DirectUrl;
                                        }

                                        if (File.Exists(mediumPath))
                                        {
                                            File.Delete(mediumPath);
                                        }
                                    }

                                    var finalResult = result with { MediumUrl = mediumUrl };
                                    uploadResults.Add(finalResult);

                                    if (!result.Success)
                                    {
                                        errors.Add(
                                            $"Upload failed: {item.FileName} - {result.Error}");
                                    }
                                    else
                                    {
                                        // Cache the successful upload
                                        _uploadCacheService.Set(contentHash, config.StorageChoice,
                                            new UploadCacheEntry(
                                                result.DirectUrl,
                                                mediumUrl,
                                                new FileInfo(item.FilePath).Length,
                                                DateTime.UtcNow));

                                        MoveToOutput(item, config, CurrentFolderPath!);

                                        if (processedPath != item.FilePath
                                            && File.Exists(processedPath))
                                        {
                                            File.Delete(processedPath);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error: {item.FileName} - {ex.Message}");
                            Log.Error(ex, "Upload error for {File}", item.FileName);
                        }

                        processedItems++;
                        _eventAggregator.GetEvent<ProgressUpdateEvent>().Publish(
                            (double)processedItems / totalItems);

                        if (config.PauseSeconds > 0)
                        {
                            await Task.Delay(config.PauseSeconds * 1000, ct);
                        }
                    }

                    // Create Telegraph page for this group
                    if (uploadResults.Any(r => r.Success))
                    {
                        try
                        {
                            var title = string.IsNullOrEmpty(group.Key) ?
                                Path.GetFileName(CurrentFolderPath) ?? "Gallery" : group.Key;
                            _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(
                                $"Creating Telegraph page: {title}...");
                            var url = await _telegraphService.CreatePageAsync(
                                title, uploadResults, config);

                            Log.Information("Telegraph page created: {Url}", url);
                            _eventAggregator.GetEvent<PageCreatedEvent>().Publish(url);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var dlgParams = new DialogParameters { { "Url", url } };
                                _dialogService.ShowDialog("UploadSuccessDialog", dlgParams, _ => { });
                            });

                            // Append to results.txt
                            var resultsPath = Path.Combine(
                                CurrentFolderPath ?? "", "results.txt");
                            var line = $"{title} : {uploadResults.Count(r => r.Success)} : {url}\n";
                            await File.AppendAllTextAsync(resultsPath, line, ct);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Telegraph page creation failed: {ex.Message}");
                            Log.Error(ex, "Telegraph page creation failed");
                        }
                    }
                }

                if (errors.Count > 0)
                {
                    var parameters = new DialogParameters
                    {
                        { "Errors", errors }
                    };
                    Application.Current.Dispatcher.Invoke(() =>
                        _dialogService.ShowDialog("ErrorSummaryDialog", parameters, _ => { }));
                }

                message = "Upload complete";
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(message);
            }
            catch (OperationCanceledException)
            {
                message = "Upload cancelled";
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Upload workflow failed");
                message = $"Upload failed: {ex.Message}";
                _eventAggregator.GetEvent<StatusUpdateEvent>().Publish(message);
            }
            finally
            {
                _isUploading = false;
                _uploadCacheService.Flush();
                _eventAggregator.GetEvent<UploadFinishedEvent>().Publish(message);
            }
        }

        private void OnCancelUpload()
        {
            _uploadCts?.Cancel();
        }

        private static void MoveToOutput(GalleryItem item, AppConfig config, string rootFolderPath)
        {
            try
            {
                var outputDir = Path.Combine(rootFolderPath, config.OutputFolder, item.SubFolder);
                Directory.CreateDirectory(outputDir);
                var destPath = Path.Combine(outputDir, item.FileName);
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                File.Move(item.FilePath, destPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to move file to output: {File}", item.FileName);
            }
        }

        public override void Destroy()
        {
            _thumbnailCts?.Cancel();
            _thumbnailCts?.Dispose();
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            base.Destroy();
        }

        // IDropTarget implementation
        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is GalleryItem)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }

        public void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is GalleryItem source)
            {
                var oldIndex = Items.IndexOf(source);
                var newIndex = dropInfo.InsertIndex;
                if (oldIndex >= 0 && oldIndex != newIndex)
                {
                    Items.Move(oldIndex, newIndex > oldIndex ? newIndex - 1 : newIndex);
                    SetSortMode("Custom");
                }
            }
        }
    }
}
