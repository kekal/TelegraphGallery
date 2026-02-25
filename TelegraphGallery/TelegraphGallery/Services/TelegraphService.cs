using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Flurl.Http;
using Serilog;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class TelegraphService : ITelegraphService
    {
        public async Task<string> CreatePageAsync(string title, List<UploadResult> results,
            AppConfig config)
        {
            try
            {
                var token = config.TelegraphAccessToken;

                // Create anonymous account if no token
                if (string.IsNullOrEmpty(token))
                {
                    var accountResponse = await "https://api.telegra.ph/createAccount"
                        .PostUrlEncodedAsync(new
                        {
                            short_name = "Gallery",
                            author_name = config.HeaderName
                        }).ConfigureAwait(false);
                    var accountJson = await accountResponse.GetJsonAsync<JsonElement>().ConfigureAwait(false);
                    token = accountJson.GetProperty("result").GetProperty("access_token").GetString()!;
                }

                // Build content
                var content = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["tag"] = "p",
                        ["children"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["tag"] = "a",
                                ["attrs"] = new Dictionary<string, string>
                                {
                                    ["href"] = config.AuthorUrl,
                                    ["target"] = "_blank"
                                },
                                ["children"] = new List<object> { config.HeaderName }
                            }
                        }
                    }
                };

                // Images
                var successResults = results.Where(r => r.Success && !string.IsNullOrEmpty(r.DirectUrl)).ToList();
                for (var i = 0; i < successResults.Count; i++)
                {
                    var result = successResults[i];
                    var displayUrl = result.MediumUrl ?? result.ThumbnailUrl ?? result.DirectUrl;
                    var hasFullVersion = displayUrl != result.DirectUrl;

                    content.Add(new Dictionary<string, object>
                    {
                        ["tag"] = "img",
                        ["attrs"] = new Dictionary<string, string>
                        {
                            ["src"] = displayUrl
                        }
                    });

                    if (hasFullVersion)
                    {
                        content.Add(new Dictionary<string, object>
                        {
                            ["tag"] = "aside",
                            ["children"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["tag"] = "a",
                                    ["attrs"] = new Dictionary<string, string>
                                    {
                                        ["href"] = result.DirectUrl,
                                        ["target"] = "_blank"
                                    },
                                    ["children"] = new List<object> { "Full size" }
                                }
                            }
                        });
                    }

                    // Separator between image blocks
                    if (i < successResults.Count - 1)
                    {
                        content.Add(new Dictionary<string, object> { ["tag"] = "hr" });
                    }
                }

                var contentJson = JsonSerializer.Serialize(content);

                var response = await "https://api.telegra.ph/createPage"
                    .PostUrlEncodedAsync(new
                    {
                        access_token = token,
                        title,
                        content = contentJson,
                        return_content = false
                    }).ConfigureAwait(false);

                var pageJson = await response.GetJsonAsync<JsonElement>().ConfigureAwait(false);
                var path = pageJson.GetProperty("result").GetProperty("path").GetString()!;

                return $"https://telegra.ph/{path}";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create Telegraph page for {Title}", title);
                throw;
            }
        }
    }
}
