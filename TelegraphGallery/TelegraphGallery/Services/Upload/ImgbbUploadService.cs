using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using Serilog;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services.Upload
{
    public class ImgbbUploadService : IUploadService
    {
        public async Task<UploadResult> UploadFileAsync(string filePath, AppConfig config, CancellationToken ct)
        {
            try
            {
                var response = await "https://api.imgbb.com/1/upload"
                    .PostMultipartAsync(mp => mp
                        .AddString("key", config.ImgbbApiKey)
                        .AddFile("image", filePath),
                    cancellationToken: ct).ConfigureAwait(false);

                var json = await response.GetJsonAsync<JsonElement>().ConfigureAwait(false);
                var imageUrl = json.GetProperty("data").GetProperty("url").GetString()!;

                return new UploadResult(true, imageUrl);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ImgBB upload failed for {FilePath}", filePath);
                return new UploadResult(false, "", Error: ex.Message);
            }
        }
    }
}
