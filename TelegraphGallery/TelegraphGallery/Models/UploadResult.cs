namespace TelegraphGallery.Models
{
    public record UploadResult(
        bool Success,
        string DirectUrl,
        string? ThumbnailUrl = null,
        string? MediumUrl = null,
        string? Error = null
    );
}
