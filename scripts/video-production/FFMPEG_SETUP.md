# Video Production Setup Guide

## ffmpeg Installation

The video stitching workflow requires ffmpeg and ffprobe to be installed and accessible. This guide covers installation options for Windows.

### Option 1: Download Portable ffmpeg (Recommended for Non-Admin)

1. Download from: https://www.gyan.dev/ffmpeg/builds/ffmpeg-git-full.7z
   - Or use official builds: https://ffmpeg.org/download.html#build-windows
2. Extract the .7z file to a local directory (e.g., `C:\tools\ffmpeg`)
3. Set the environment variable before running the stitching script:
   ```powershell
   $env:FFMPEG_PATH = "C:\tools\ffmpeg\bin\ffmpeg.exe"
   $env:FFPROBE_PATH = "C:\tools\ffmpeg\bin\ffprobe.exe"
   ```

### Option 2: Chocolatey Installation (Admin Required)

```powershell
choco install ffmpeg
```

After installation, ffmpeg will be in PATH and can be used directly.

### Option 3: Scoop Installation (Non-Admin Friendly)

```powershell
scoop install ffmpeg
```

After installation, use directly:
```powershell
$env:FFMPEG_PATH = "$(scoop which ffmpeg)"
$env:FFPROBE_PATH = "$(scoop which ffprobe)"
```

### Option 4: winget Installation

```powershell
winget install ffmpeg
```

### Verify Installation

After installing ffmpeg, verify it's working:

```powershell
ffmpeg -version
ffprobe -version
```

If both commands return version information, ffmpeg is ready to use.

## Running the Video Stitching Script

Once ffmpeg is installed, generate the final video:

```powershell
cd docs\testing\video-production\scenarios\video-1-skill-journey

# If using portable ffmpeg, set the path
$env:FFMPEG_PATH = "C:\path\to\ffmpeg.exe"

# Run the stitching script
& ..\..\..\..\..\scripts\video-production\stitch-video-1-skill-journey.ps1
```

## Expected Output

The script generates `recordings\final\video-1-skill-journey-final.mp4` with:
- Duration: ~46 seconds
- Resolution: 1280×720
- Codec: H.264
- Format: MP4 container

## Troubleshooting

**"ffmpeg not found" error:**
- Check ffmpeg is installed and in PATH, or
- Set `$env:FFMPEG_PATH` to the explicit path to ffmpeg.exe

**"Title card video failed" error:**
- The script uses ffmpeg `drawtext`, not SVG rendering, so no librsvg/ImageMagick dependency is required.
- Verify `drawtext` is available with: `ffmpeg -filters | Select-String drawtext`

**Low video quality:**
- Adjust encoder settings in the stitching script (e.g., `-preset faster` for speed, `-preset slower` for better quality)
- Current settings use `-preset veryfast` for quick processing

## File Locations

- **Stitching script:** `scripts\video-production\stitch-video-1-skill-journey.ps1`
- **Title card source:** `docs\testing\video-production\scenarios\video-1-skill-journey\assets\source\title-card.svg`
- **Raw Playwright capture:** `docs\testing\video-production\scenarios\video-1-skill-journey\recordings\raw\fab2585722cf8dd38383cfdf3da911a6.webm`
- **Final output:** `docs\testing\video-production\scenarios\video-1-skill-journey\recordings\final\video-1-skill-journey-final.mp4`
