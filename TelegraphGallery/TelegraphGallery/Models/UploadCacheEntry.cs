using System;

namespace TelegraphGallery.Models
{
    public record UploadCacheEntry(
        string DirectUrl,
        string? MediumUrl,
        long FileSize,
        DateTime CachedAtUtc
    );
}
