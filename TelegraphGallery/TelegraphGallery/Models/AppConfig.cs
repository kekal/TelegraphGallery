namespace TelegraphGallery.Models
{
    public class AppConfig
    {
        // Default values
        public const int DefaultMaxDimension = 5000;
        public const int DefaultTotalDimensionThreshold = 10000;
        public const long DefaultMaxFileSize = 5_000_000; // 5 MB
        public const int DefaultPauseSeconds = 2;
        public const int DefaultDuplicateThreshold = 5;
        public const int DefaultThumbnailSize = 150;

        // Storage
        public string StorageChoice { get; set; } = "imgbb";

        // Telegraph
        public string TelegraphAccessToken { get; set; } = "";
        public string AuthorUrl { get; set; } = "https://my_page";
        public string HeaderName { get; set; } = "My albums page";

        // ImgBB
        public string ImgbbApiKey { get; set; } = "";

        // Cyberdrop
        public string CyberdropToken { get; set; } = "";
        public string CyberdropAlbumId { get; set; } = "";

        // Image processing
        public int MaxWidth { get; set; } = DefaultMaxDimension;
        public int MaxHeight { get; set; } = DefaultMaxDimension;
        public int TotalDimensionThreshold { get; set; } = DefaultTotalDimensionThreshold;
        public long MaxFileSize { get; set; } = DefaultMaxFileSize;

        // Upload
        public int PauseSeconds { get; set; } = DefaultPauseSeconds;

        // FileSystem
        public string OutputFolder { get; set; } = "old";

        // Duplicates
        public int DuplicateThreshold { get; set; } = DefaultDuplicateThreshold;

        // UI
        public int ThumbnailSize { get; set; } = DefaultThumbnailSize;
        public string SortMode { get; set; } = "Name";

        /// <summary>
        /// Creates a shallow copy of this config. All properties are value types
        /// or immutable strings, so a shallow copy is a safe independent clone.
        /// </summary>
        public AppConfig Clone()
        {
            return new AppConfig
            {
                StorageChoice = StorageChoice,
                TelegraphAccessToken = TelegraphAccessToken,
                AuthorUrl = AuthorUrl,
                HeaderName = HeaderName,
                ImgbbApiKey = ImgbbApiKey,
                CyberdropToken = CyberdropToken,
                CyberdropAlbumId = CyberdropAlbumId,
                MaxWidth = MaxWidth,
                MaxHeight = MaxHeight,
                TotalDimensionThreshold = TotalDimensionThreshold,
                MaxFileSize = MaxFileSize,
                PauseSeconds = PauseSeconds,
                OutputFolder = OutputFolder,
                DuplicateThreshold = DuplicateThreshold,
                ThumbnailSize = ThumbnailSize,
                SortMode = SortMode,
            };
        }
    }
}
