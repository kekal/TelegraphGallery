using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Serilog;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services.Upload
{
    public class CyberdropUploadService : IUploadService
    {
        public async Task<UploadResult> UploadFileAsync(string filePath, AppConfig config, CancellationToken ct)
        {
            try
            {
                // Get server URL
                var nodeResponse = await "https://cyberdrop.me/api/node"
                    .WithHeader("token", config.CyberdropToken)
                    .GetJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);

                var serverUrl = nodeResponse.GetProperty("url").GetString()!;

                // Upload file
                var response = await serverUrl
                    .WithHeader("token", config.CyberdropToken)
                    .WithHeader("albumid", config.CyberdropAlbumId)
                    .PostMultipartAsync(mp => mp
                        .AddFile("files[]", filePath),
                    cancellationToken: ct).ConfigureAwait(false);

                var json = await response.GetJsonAsync<JsonElement>().ConfigureAwait(false);
                var files = json.GetProperty("files");
                var file = files.EnumerateArray().First();
                var directUrl = file.GetProperty("url").GetString()!;

                // Get thumbnail
                string? thumbUrl = null;
                try
                {
                    var uploadsResponse = await "https://cyberdrop.me/api/uploads"
                        .WithHeader("token", config.CyberdropToken)
                        .GetJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);

                    var uploads = uploadsResponse.GetProperty("files");
                    var slug = Path.GetFileNameWithoutExtension(directUrl.Split('/').Last());
                    foreach (var upload in uploads.EnumerateArray())
                    {
                        var uploadUrl = upload.GetProperty("url").GetString() ?? "";
                        if (uploadUrl.Contains(slug))
                        {
                            thumbUrl = upload.GetProperty("thumb").GetString();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to fetch Cyberdrop thumbnail");
                }

                return new UploadResult(true, directUrl, thumbUrl);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cyberdrop upload failed for {FilePath}", filePath);
                return new UploadResult(false, "", Error: ex.Message);
            }
        }
    }
}
