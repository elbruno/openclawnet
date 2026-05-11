<#
.SYNOPSIS
    Stitch Video 1 (Skill-Powered Chat Journey) with branded intro title card and outro frame hold.
    
.DESCRIPTION
    This script processes the raw Playwright WebM capture and produces a final MP4 with:
    - Branded title card intro (3 seconds): OpenClawNet logo + "Skill-Powered Chat" + purpose + steps
      * Uses product-grade dark palette (deep blue-to-purple gradient)
      * Enhanced visual hierarchy with separated logo/title/description/steps
    - Trimmed raw content (skip ~7-8 seconds of dead startup frame)
    - Final frame hold (8-10 seconds) for viewer absorption
    
    Requires ffmpeg (globally available or via FFMPEG_PATH environment variable).
    
.PARAMETER RawWebmPath
    Path to the raw WebM file from Playwright. Defaults to recordings\raw\fab2585722cf8dd38383cfdf3da911a6.webm
    
.PARAMETER OutputMp4Path
    Path to write the final MP4. Defaults to recordings\final\video-1-skill-journey-final.mp4
    
.PARAMETER TitleCardSvgPath
    Path to the title card SVG source. Defaults to assets\source\title-card.svg
    
.PARAMETER TrimStartSeconds
    Seconds to trim from the start of raw capture (skip dead frame). Defaults to 7.
    
.PARAMETER IntroCardDuration
    Duration in seconds for title card. Defaults to 3.
    
.PARAMETER OutroHoldDuration
    Duration in seconds for final frame hold. Defaults to 9.
    
.PARAMETER FfmpegPath
    Explicit path to ffmpeg binary. If not provided, uses $env:FFMPEG_PATH or searches PATH.

.PARAMETER FfprobePath
    Explicit path to ffprobe binary. If not provided, uses $env:FFPROBE_PATH, ffmpeg's directory, or searches PATH.
    
.EXAMPLE
    .\stitch-video-1-skill-journey.ps1
    
.EXAMPLE
    $env:FFMPEG_PATH = "C:\tools\ffmpeg\bin\ffmpeg.exe"
    .\stitch-video-1-skill-journey.ps1
    
.NOTES
    - Temporary files are created in the output directory and cleaned up on success.
    - On error, temporary files are preserved for debugging.
    - Verify the final MP4 duration meets expectations (~49-52 seconds for this video).
    - Title card uses ffmpeg drawtext filters for robust, long-term compatibility.
#>

param(
    [string]$RawWebmPath = "recordings\raw\fab2585722cf8dd38383cfdf3da911a6.webm",
    [string]$OutputMp4Path = "recordings\final\video-1-skill-journey-final.mp4",
    [string]$TitleCardSvgPath = "assets\source\title-card.svg",
    [int]$TrimStartSeconds = 7,
    [int]$IntroCardDuration = 3,
    [int]$OutroHoldDuration = 9,
    [string]$FfmpegPath = $null,
    [string]$FfprobePath = $null
)

$ErrorActionPreference = "Stop"

function Get-FfmpegPath {
    if ($FfmpegPath) {
        if (Test-Path $FfmpegPath) {
            return $FfmpegPath
        }
        throw "FFMPEG_PATH provided but not found: $FfmpegPath"
    }
    
    if ($env:FFMPEG_PATH -and (Test-Path $env:FFMPEG_PATH)) {
        return $env:FFMPEG_PATH
    }
    
    $result = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($result) {
        return $result.Source
    }
    
    throw @"
ffmpeg not found. Please:
  1. Install ffmpeg from https://ffmpeg.org/download.html
  2. Add ffmpeg to PATH, or
  3. Set FFMPEG_PATH environment variable: `$env:FFMPEG_PATH = 'C:\path\to\ffmpeg.exe'
  4. Run this script again.
"@
}

function Get-FfprobePath {
    if ($FfprobePath) {
        if (Test-Path $FfprobePath) {
            return $FfprobePath
        }
        throw "FFPROBE_PATH provided but not found: $FfprobePath"
    }

    if ($env:FFPROBE_PATH -and (Test-Path $env:FFPROBE_PATH)) {
        return $env:FFPROBE_PATH
    }

    $ffmpegDir = Split-Path (Get-FfmpegPath)
    $ffprobePath = Join-Path $ffmpegDir "ffprobe.exe"
    if (Test-Path $ffprobePath) {
        return $ffprobePath
    }
    
    $result = Get-Command ffprobe -ErrorAction SilentlyContinue
    if ($result) {
        return $result.Source
    }
    
    throw "ffprobe not found. Set FFPROBE_PATH or ensure ffmpeg installation includes ffprobe."
}

try {
    Write-Host "Video 1 (Skill-Powered Chat Journey) Stitching Script" -ForegroundColor Cyan
    Write-Host ""
    
    # Resolve paths
    $ffmpeg = Get-FfmpegPath
    $ffprobe = Get-FfprobePath
    Write-Host "ffmpeg: $ffmpeg"
    Write-Host "ffprobe: $ffprobe"
    Write-Host ""
    
    # Validate input files
    if (-not (Test-Path $RawWebmPath)) {
        throw "Raw WebM not found: $RawWebmPath"
    }
    Write-Host "Raw WebM: $RawWebmPath"
    
    if (-not (Test-Path $TitleCardSvgPath)) {
        throw "Title card SVG not found: $TitleCardSvgPath"
    }
    Write-Host "Title card SVG: $TitleCardSvgPath"
    Write-Host ""
    
    # Create output directory if needed
    $outDir = Split-Path $OutputMp4Path
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
        Write-Host "Created output directory: $outDir"
    }
    
    # Generate temporary files in output directory
    $tmpDir = $outDir
    $titleCardVid = Join-Path $tmpDir "title-card-temp.mp4"
    $trimmedVid = Join-Path $tmpDir "trimmed-temp.mp4"
    $finalFramePng = Join-Path $tmpDir "final-frame-temp.png"
    $outroVid = Join-Path $tmpDir "outro-temp.mp4"
    $concatFile = Join-Path $tmpDir "concat-list.txt"
    
    # Step 1: Create a title-card video directly with ffmpeg drawtext.
    # The SVG remains the tracked design source; drawtext avoids requiring librsvg/ImageMagick.
    # Uses product-grade dark palette (deep blue-to-purple gradient: #052767 → #3a0647)
    Write-Host "Step 2: Creating $IntroCardDuration-second title card video..." -ForegroundColor Green
    $fontRegular = "C\:/Windows/Fonts/segoeui.ttf"
    $fontBold = "C\:/Windows/Fonts/segoeuib.ttf"
     
    # Draw dark blue-to-purple background gradient (product palette: #052767 → #3a0647)
    $gradientFilter = "format=rgba," +
        "drawbox=x=0:y=0:w=1280:h=360:color=0x052767FF:t=fill," +
        "drawbox=x=0:y=360:w=1280:h=360:color=0x3a0647FF:t=fill"
     
    # Text overlays with enhanced visual hierarchy
    $titleFilter = $gradientFilter + "," +
        "drawtext=fontfile='$fontBold':text='OpenClawNet':fontcolor=0x58A6FF:fontsize=40:x=(w-text_w)/2:y=120," +
        "drawtext=fontfile='$fontBold':text='Skill-Powered Chat':fontcolor=white:fontsize=54:x=(w-text_w)/2:y=185," +
        "drawtext=fontfile='$fontRegular':text='Create a skill. Enable it for an agent. Chat and see structured output.':fontcolor=0xE6EDEA:fontsize=26:x=(w-text_w)/2:y=300," +
        "drawtext=fontfile='$fontRegular':text='Steps\: Skills page → enable skill → new chat → formatted reply':fontcolor=0xD8E6FF:fontsize=20:x=(w-text_w)/2:y=385"
     
    & $ffmpeg -y -f lavfi -i "color=c=0x000000:s=1280x720:d=$IntroCardDuration:r=30" -vf $titleFilter -c:v libx264 -preset veryfast -tune stillimage -pix_fmt yuv420p -r 30 $titleCardVid 2>&1 | Out-Null
    if (-not (Test-Path $titleCardVid)) {
        throw "Failed to create title card video"
    }
    Write-Host "  ✓ Title card video created: $titleCardVid"
    
    # Step 3: Trim and normalize raw WebM (skip first N seconds).
    # Re-encode so all concat segments share H.264/yuv420p/30fps.
    Write-Host "Step 3: Trimming and normalizing raw WebM (skipping first $TrimStartSeconds seconds)..." -ForegroundColor Green
    & $ffmpeg -y -ss $TrimStartSeconds -i $RawWebmPath -vf "scale=1280:720,fps=30" -c:v libx264 -preset veryfast -pix_fmt yuv420p -an $trimmedVid 2>&1 | Out-Null
    if (-not (Test-Path $trimmedVid)) {
        throw "Failed to trim WebM"
    }
    Write-Host "  ✓ Trimmed video created: $trimmedVid"
    
    # Step 4: Get duration of trimmed video to extract final frame timing
    Write-Host "Step 4: Extracting final frame for outro..." -ForegroundColor Green
    & $ffmpeg -y -sseof -0.1 -i $trimmedVid -frames:v 1 -vf scale=1280:720 $finalFramePng 2>&1 | Out-Null
    if (-not (Test-Path $finalFramePng)) {
        throw "Failed to extract final frame"
    }
    Write-Host "  ✓ Final frame extracted: $finalFramePng"
    
    # Step 5: Create outro video from final frame (hold for N seconds)
    Write-Host "Step 5: Creating $OutroHoldDuration-second outro hold..." -ForegroundColor Green
    & $ffmpeg -y -loop 1 -i $finalFramePng -c:v libx264 -preset veryfast -tune stillimage -pix_fmt yuv420p -t $OutroHoldDuration -r 30 $outroVid 2>&1 | Out-Null
    if (-not (Test-Path $outroVid)) {
        throw "Failed to create outro video"
    }
    Write-Host "  ✓ Outro video created: $outroVid"
    
    # Step 6: Create concat demux file
    Write-Host "Step 6: Concatenating segments..." -ForegroundColor Green
    @"
file '$titleCardVid'
file '$trimmedVid'
file '$outroVid'
"@ | Set-Content $concatFile -Encoding UTF8
    
    # Step 7: Concatenate all segments into final MP4
    & $ffmpeg -y -f concat -safe 0 -i $concatFile -c copy -pix_fmt yuv420p $OutputMp4Path 2>&1 | Out-Null
    if (-not (Test-Path $OutputMp4Path)) {
        throw "Failed to create final MP4"
    }
    Write-Host "  ✓ Final MP4 created: $OutputMp4Path"
    
    # Step 8: Verify output
    Write-Host "Step 7: Verifying final output..." -ForegroundColor Green
    $finalDurationJson = & $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $OutputMp4Path 2>&1
    $finalDuration = [double]$finalDurationJson
    $finalFileSize = (Get-Item $OutputMp4Path).Length / 1MB
    
    Write-Host "  ✓ Final duration: $([Math]::Round($finalDuration, 1)) seconds"
    Write-Host "  ✓ Final file size: $([Math]::Round($finalFileSize, 1)) MB"
    Write-Host ""
    
    if ($finalDuration -lt 30) {
        Write-Warning "Final video duration seems short (< 30s). Check logs for issues."
    }
    
    # Cleanup temporary files
    Write-Host "Cleaning up temporary files..." -ForegroundColor Green
    Remove-Item $titleCardVid, $trimmedVid, $finalFramePng, $outroVid, $concatFile -Force -ErrorAction SilentlyContinue
    Write-Host "  ✓ Temporary files cleaned up"
    
    Write-Host ""
    Write-Host "✓ Video stitching complete!" -ForegroundColor Green
    Write-Host "Output: $OutputMp4Path"
    Write-Host "Duration: $([Math]::Round($finalDuration, 1)) seconds"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Review the video in your media player"
    Write-Host "  2. If satisfied, commit the output (manually if needed)"
    Write-Host "  3. Update README.md and VIDEO_EXPLANATION.md with timing"
}
catch {
    Write-Error "Video stitching failed: $_"
    Write-Host ""
    Write-Host "Temporary files preserved for debugging at: $tmpDir"
    exit 1
}
