# Telegraph Gallery

A Windows desktop application for batch uploading images and videos to cloud storage services (ImgBB, Cyberdrop, IPFS) and automatically publishing them as Telegraph pages.

Built with WPF, .NET 8, and Prism MVVM.

## Features

- **Batch Upload** — scan folders recursively, upload all images/videos in one click
- **Multiple Storage Backends** — ImgBB, Cyberdrop, IPFS
- **Telegraph Publishing** — auto-generates Telegraph pages with thumbnails and full-size links
- **Duplicate Detection** — perceptual hashing (pHash) finds visually similar images
- **Image Processing** — auto-resize, compress, and convert images before upload
- **Smart Caching** — skips re-uploading identical files based on content hash

## Requirements

- Windows x64
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

```bash
# build
dotnet build

# run
dotnet run --project TelegraphGallery/TelegraphGallery.csproj

# publish self-contained single-file release (uses Properties/PublishProfiles/FolderProfile.pubxml)
dotnet publish TelegraphGallery/TelegraphGallery.csproj -p:PublishProfile=FolderProfile
```

## Quick Start

1. **Launch the app** and click the Settings button to open the settings panel.

2. **Choose a storage service** and enter your credentials:

   | Service   | Required Fields          |
   |-----------|--------------------------|
   | ImgBB     | API Key                  |
   | Cyberdrop | Token + Album ID         |
   | IPFS      | _(no credentials needed)_|

3. **Open a folder** — click the folder button or drag a folder into the window. The app scans for images (`.jpg`, `.png`, `.gif`, `.bmp`, `.webp`, `.heic`) and videos (`.mp4`, `.avi`, `.mov`, `.mkv`, `.wmv`, `.webm`).

4. **Review thumbnails** — exclude unwanted items by clicking them, reorder with drag & drop, pick a sort mode.

5. **Find duplicates**  — click the duplicates button. Similar images are highlighted in matching colors. Exclude the ones you don't need.

6. **Upload** — click the upload button. The app will:
   - Resize/compress images that exceed limits
   - Upload to your chosen storage service
   - Generate a Telegraph page with gallery
   - Save results to `results.txt`

7. **Copy the link** — after upload, use the copy/open buttons in the toolbar to get the Telegraph page URL.

## Configuration

Settings are stored in `config.ini` (created on first run, next to the executable). You can edit it in the app's settings panel or directly in a text editor.

```ini
[Storage]
Choice = imgbb          ; imgbb | cyberdrop | ipfs

[ImgBB]
ApiKey = YOUR_API_KEY

[Cyberdrop]
Token = YOUR_TOKEN
AlbumId = YOUR_ALBUM_ID

[Telegraph]
AccessToken =           ; leave empty to auto-create on first upload
AuthorUrl = https://my_page
HeaderName = My albums page

[Image]
MaxWidth = 5000
MaxHeight = 5000
TotalDimensionThreshold = 10000
MaxFileSize = 5000000   ; bytes

[Upload]
PauseSeconds = 2        ; delay between uploads

[FileSystem]
OutputFolder = old      ; subfolder to move uploaded files into

[Duplicates]
Threshold = 5           ; max % difference to consider duplicates

[UI]
ThumbnailSize = 150
SortMode = Name         ; Name | FileTimestamp | ExifDate | Custom
```

## Usage Examples

### Upload a folder of photos to ImgBB

```
1. Set Storage → imgbb, enter your ImgBB API key
2. Click "Open Folder" → select C:\Photos\vacation
3. Sort/reorder if needed.
4. Click "Upload All"
5. Click the Telegraph link from the toolbar
```

### Find and remove duplicates before uploading

```
1. Open a folder with many similar photos
2. Click "Find Duplicates" — groups are color-coded
3. Right-Click on duplicates to exclude them (they become semi-transparent)
4. Upload only the unique images
```

### Upload with custom image limits

```ini
; In config.ini — limit images to 2000px and 2MB
[Image]
MaxWidth = 2000
MaxHeight = 2000
MaxFileSize = 2000000
```

### Re-upload if something failed/interrupted

You can upload your folder again. Files that have already been uploaded will not be re-uploaded.

## Running Tests

Most tests (unit, image processing, duplicate detection, etc.) run without any setup:

```bash
dotnet test
```

Some integration tests upload to ImgBB and require an API key. These tests are marked as `[SkippableFact]` and will be **skipped automatically** if no key is provided.

To run them, create the settings file:

```
Tests/TelegraphGallery.DuplicateDetection.Tests/test.runsettings
```

with the following content:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <IMGBB_API_KEY>your_imgbb_api_key_here</IMGBB_API_KEY>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

Then run tests with the settings file:

```bash
dotnet test --settings Tests/TelegraphGallery.DuplicateDetection.Tests/test.runsettings
```

> **Note:** `test.runsettings` is git-ignored — never commit API keys to the repository.

## Temp & Cache Files

Upload results are cached in `upload_cache.json` to avoid re-uploading identical files. You can clear the cache by deleting this file or using the "Clear Cache" button on the toolbar. Cache cleare automatically after a month.


| Location | Purpose |
|----------|---------|
| `%TEMP%/TelegraphGallery/thumbs/` | Thumbnail cache |
| `%TEMP%/TelegraphGallery/processed/` | Temporary processed images |
| `%TEMP%/TelegraphGallery/upload_cache.json` | Upload result cache |
| `log.txt` (app directory) | Daily rolling log file |
| `results.txt` (upload folder) | Upload results: `title : count : url` |
