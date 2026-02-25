using System;
using System.IO;
using IniParser;
using IniParser.Model;
using Serilog;
using TelegraphGallery.Models;
using TelegraphGallery.Services.Interfaces;

namespace TelegraphGallery.Services
{
    public class ConfigService : IConfigService
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config.ini");

        private readonly FileIniDataParser _parser = new();
        private readonly object _lock = new();
        private AppConfig? _cachedConfig;

        public AppConfig Load()
        {
            lock (_lock)
            {
                if (_cachedConfig != null)
                    return _cachedConfig.Clone();

                var config = new AppConfig();
                if (!File.Exists(ConfigPath))
                {
                    _cachedConfig = config;
                    Save(config);
                    return config.Clone();
                }

                try
                {
                    var data = _parser.ReadFile(ConfigPath);

                    config.StorageChoice = Get(data, "Storage", "Choice", config.StorageChoice);
                    config.TelegraphAccessToken = Get(data, "Telegraph", "AccessToken", config.TelegraphAccessToken);
                    config.AuthorUrl = Get(data, "Telegraph", "AuthorUrl", config.AuthorUrl);
                    config.HeaderName = Get(data, "Telegraph", "HeaderName", config.HeaderName);
                    config.ImgbbApiKey = Get(data, "ImgBB", "ApiKey", config.ImgbbApiKey);
                    config.CyberdropToken = Get(data, "Cyberdrop", "Token", config.CyberdropToken);
                    config.CyberdropAlbumId = Get(data, "Cyberdrop", "AlbumId", config.CyberdropAlbumId);
                    config.MaxWidth = GetInt(data, "Image", "MaxWidth", config.MaxWidth);
                    config.MaxHeight = GetInt(data, "Image", "MaxHeight", config.MaxHeight);
                    config.TotalDimensionThreshold = GetInt(data, "Image", "TotalDimensionThreshold", config.TotalDimensionThreshold);
                    config.MaxFileSize = GetLong(data, "Image", "MaxFileSize", config.MaxFileSize);
                    config.PauseSeconds = GetInt(data, "Upload", "PauseSeconds", config.PauseSeconds);
                    config.OutputFolder = Get(data, "FileSystem", "OutputFolder", config.OutputFolder);
                    config.DuplicateThreshold = GetInt(data, "Duplicates", "Threshold", config.DuplicateThreshold);
                    config.ThumbnailSize = GetInt(data, "UI", "ThumbnailSize", config.ThumbnailSize);
                    config.SortMode = Get(data, "UI", "SortMode", config.SortMode);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load config, using defaults");
                }

                _cachedConfig = config;
                return config.Clone();
            }
        }

        public void Save(AppConfig config)
        {
            lock (_lock)
            {
                try
                {
                    var data = new IniData();

                    data["Storage"]["Choice"] = config.StorageChoice;
                    data["Telegraph"]["AccessToken"] = config.TelegraphAccessToken;
                    data["Telegraph"]["AuthorUrl"] = config.AuthorUrl;
                    data["Telegraph"]["HeaderName"] = config.HeaderName;
                    data["ImgBB"]["ApiKey"] = config.ImgbbApiKey;
                    data["Cyberdrop"]["Token"] = config.CyberdropToken;
                    data["Cyberdrop"]["AlbumId"] = config.CyberdropAlbumId;
                    data["Image"]["MaxWidth"] = config.MaxWidth.ToString();
                    data["Image"]["MaxHeight"] = config.MaxHeight.ToString();
                    data["Image"]["TotalDimensionThreshold"] = config.TotalDimensionThreshold.ToString();
                    data["Image"]["MaxFileSize"] = config.MaxFileSize.ToString();
                    data["Upload"]["PauseSeconds"] = config.PauseSeconds.ToString();
                    data["FileSystem"]["OutputFolder"] = config.OutputFolder;
                    data["Duplicates"]["Threshold"] = config.DuplicateThreshold.ToString();
                    data["UI"]["ThumbnailSize"] = config.ThumbnailSize.ToString();
                    data["UI"]["SortMode"] = config.SortMode;

                    _parser.WriteFile(ConfigPath, data);
                    _cachedConfig = config.Clone();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save config");
                }
            }
        }

        private static string Get(IniData data, string section, string key, string defaultValue)
        {
            var value = data[section]?[key];
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private static int GetInt(IniData data, string section, string key, int defaultValue)
        {
            var value = data[section]?[key];
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        private static long GetLong(IniData data, string section, string key, long defaultValue)
        {
            var value = data[section]?[key];
            return long.TryParse(value, out var result) ? result : defaultValue;
        }
    }
}
