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
    public class IpfsUploadService : IUploadService
    {
        public async Task<UploadResult> UploadFileAsync(string filePath, AppConfig config, CancellationToken ct)
        {
            try
            {
                var response = await "http://127.0.0.1:5001/api/v0/add"
                    .PostMultipartAsync(mp => mp.AddFile("file", filePath),
                    cancellationToken: ct).ConfigureAwait(false);

                var json = await response.GetJsonAsync<JsonElement>().ConfigureAwait(false);
                var cid = json.GetProperty("Hash").GetString()!;

                // Pin the file
                await $"http://127.0.0.1:5001/api/v0/pin/add?arg={cid}"
                    .PostAsync(cancellationToken: ct).ConfigureAwait(false);

                var publicUrl = $"https://ipfs.io/ipfs/{cid}";

                return new UploadResult(true, publicUrl);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IPFS upload failed for {FilePath}", filePath);
                return new UploadResult(false, "", Error: ex.Message);
            }
        }
    }
}
