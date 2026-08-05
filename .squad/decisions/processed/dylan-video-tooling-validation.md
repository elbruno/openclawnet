# Video 1 Tooling Runtime Failure & Validation Analysis
**Date:** 2026-05-09 | **Reviewer:** Dylan (Tester) | **Status:** FINDINGS + FIXES NEEDED

## Executive Summary
The Video 1 stitching scripts contain **8 concrete runtime failure modes** and **no deterministic MP4 verification**. The generated MP4 can be verified via `ffprobe`, but the scripts lack this validation. Existing output passes all codec/format checks; scripts will fail on Windows paths with spaces or if run from wrong directory.

---

## Critical Findings

### 1. **Relative Path Resolution Failure**
- **Location:** Both scripts use relative paths (`..\scenarios\...`)
- **Risk:** Scripts assume execution from `video-production\scripts` directory
- **Impact:** Running from any other directory silently fails with "file not found"
- **Evidence:** Lines 57-58, 147-149 in `stitch-video-1-skill-journey.ps1`
- **Test Needed:** Run script from wrong directory, verify error handling

### 2. **Windows Paths with Spaces Break concat Demux**
- **Location:** Lines 251-254 (concat file generation)
- **Risk:** Paths written to concat-list.txt without escaping
- **Impact:** If output directory contains spaces, ffmpeg concat demux parser fails
- **Evidence:** `file '$titleCardVid'` without quotes in concat list
- **Test Case:**
  ```powershell
  # Should fail with space in path
  .\stitch-video-1-skill-journey.ps1 -OutputMp4Path "..\scenarios\video-1-skill-journey\my videos\final.mp4"
  ```

### 3. **Subtitle Filter Path Escaping Incomplete (Line 282-284)**
- **Location:** Narration SRT filter construction
- **Risk:** Escapes backslashes/colons/quotes but NOT spaces
- **Impact:** SRT file with spaces in path breaks ffmpeg subtitle filter
- **Evidence:** `$srtEscaped = $NarrationSrtPath -replace '\\', '/' -replace ':' '\:' -replace "'", "'\\''"`
- **Missing:** Space handling or path quoting

### 4. **Invoke-Expression with User-Supplied Paths (Line 287)**
- **Location:** Audio/caption handling uses `Invoke-Expression` on constructed command
- **Risk:** If paths contain `$`, backticks, or other PowerShell metacharacters, injection possible
- **Impact:** Command execution error or unexpected behavior
- **Alternative:** Should use array-based invocation (`& $ffmpeg -y -i $preAudioVid ...` without Invoke-Expression)

### 5. **No Deterministic Output Verification**
- **Current:** Lines 300-311 check only file exists, non-zero size, and duration > 20s
- **Missing:** No codec, resolution, frame rate, or pixel format validation
- **Risk:** Corrupted/wrong-format MP4 ships as "successful"
- **Impact:** Users get video that looks fine but isn't broadcast-ready

### 6. **Duration Validation Only Warns**
- **Location:** Line 309-311
- **Issue:** `if ($finalDuration -lt 20) { Write-Warning ... }` but script continues
- **Risk:** Video with 15-second duration (clearly wrong) succeeds
- **Should:** Fail if duration < 20s or outside expected range (30-50s)

### 7. **Temp File Cleanup Incomplete on Error**
- **Location:** Line 315 (error block), line 296 (pre-audio cleanup)
- **Issue:** Single `Remove-Item` call with multiple files; if one missing, others silently skip
- **Better:** Loop with individual `-ErrorAction SilentlyContinue` per file

### 8. **No Verification of Font Availability**
- **Location:** Lines 198-199 (Segoe UI fonts)
- **Risk:** If fonts missing, ffmpeg `drawtext` fails silently on some systems
- **Mitigation:** README mentions checking fonts, but script doesn't verify before title card generation
- **Test Case:** On system without Segoe UI, title card fails

---

## Test Results: Existing MP4 Validation

Tested `video-1-skill-journey-final.mp4` (2026-05-09 10:49:58) with `ffprobe`:

| Property | Expected | Actual | Status |
|----------|----------|--------|--------|
| Duration | ~33s | 33.0s | ✅ PASS |
| Video Codec | h264 | h264 | ✅ PASS |
| Resolution | 1280x720 | 1280x720 | ✅ PASS |
| Frame Rate | 30fps | 30/1 | ✅ PASS |
| Pixel Format | yuv420p | yuv420p | ✅ PASS |
| Audio Stream | None (video-only) | None | ✅ PASS |

**Conclusion:** Output format is correct; scripts generate valid MP4 when paths/fonts cooperate.

---

## Validation Commands (Deterministic Testing)

These commands should be added to script post-generation verification:

```powershell
# Validate final MP4 meets broadcast specs
$ffprobe = "path/to/ffprobe.exe"
$mp4 = "path/to/video-1-skill-journey-final.mp4"

# Check duration is in acceptable range (30-50 seconds for this video)
$duration = [double](& $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $mp4)
if ($duration -lt 30 -or $duration -gt 50) {
    throw "Duration out of range: $duration seconds (expected 30-50)"
}

# Verify video codec and format
$videoInfo = & $ffprobe -v error -select_streams v:0 `
    -show_entries stream=codec_name,width,height,r_frame_rate,pix_fmt `
    -of default=noprint_wrappers=1 $mp4 | ConvertFrom-StringData

if ($videoInfo['codec_name'] -ne 'h264' -or $videoInfo['width'] -ne '1280' -or $videoInfo['height'] -ne '720') {
    throw "Video format mismatch: got $($videoInfo | ConvertTo-Json)"
}

if ($videoInfo['pix_fmt'] -ne 'yuv420p') {
    throw "Pixel format must be yuv420p, got: $($videoInfo['pix_fmt'])"
}
```

---

## Fixes Required (Priority Order)

### 🔴 **CRITICAL** 
1. **Fix concat path escaping** (Line 251-254)
   - Wrap paths in double quotes in concat-list.txt
   - Test with spaces in directory names

2. **Fix subtitle filter escaping** (Line 282-284)
   - Add space handling or full path quoting
   - Replace `Invoke-Expression` with array-based call

3. **Add deterministic MP4 verification** (After line 303)
   - Validate codec, resolution, FPS, pixel format
   - Fail if duration outside acceptable range (not just warn)

### 🟡 **HIGH**
4. **Resolve relative paths robustly** 
   - Use `$PSScriptRoot` to build absolute paths
   - Document that script must run from repo root or scripts directory

5. **Add font availability check** (Before line 213)
   - Verify Segoe UI fonts exist before title card generation
   - Provide fallback or clear error message

### 🟢 **MEDIUM**
6. **Improve error handling for temp files**
   - Loop through cleanup files individually
   - Preserve all temps on error for debugging (current behavior OK)

7. **Update setup-and-stitch.ps1**
   - Pass through FfmpegPath/FfprobePath parameters
   - Currently only checks for ffmpeg, doesn't validate ffprobe

---

## Recommended Test Suite

```powershell
# test-video-stitching.ps1
$testCases = @(
    @{ Name = "Basic stitch"; Params = @{} },
    @{ Name = "With narration"; Params = @{ NarrationWavPath = "...\narration-script.wav" } },
    @{ Name = "Spaces in output path"; Params = @{ OutputMp4Path = "..\...\my videos\final.mp4" } },
    @{ Name = "Custom trim time"; Params = @{ TrimStartSeconds = 15 } },
    @{ Name = "Longer outro"; Params = @{ OutroHoldDuration = 12 } },
)

# Validation checks:
# - Output MP4 exists and > 1MB
# - Duration in range (30-50s)
# - Codec = h264, Resolution = 1280x720, FPS = 30, Format = yuv420p
# - If narration: audio stream exists, codec = aac
# - Exit code = 0
```

---

## Environment Notes
- **ffmpeg location:** Session-local at `%TEMP%\openclawnet-video-ffmpeg\node_modules\@ffmpeg-installer\win32-x64\ffmpeg.exe`
- **ffprobe location:** Session-local at `%TEMP%\openclawnet-video-ffmpeg\node_modules\@ffprobe-installer\win32-x64\ffprobe.exe`
- **Tested on:** Windows 10/11, PowerShell 5.1+
- **Logo PNG:** Present at `docs\design\assets\webapp\header-logo.png` (verified)

---

## Next Steps
1. ✅ Findings documented (this file)
2. ⏳ **Fix concat and subtitle escaping** → Needs code review from Milchick
3. ⏳ **Add deterministic MP4 validation** → Can be PR-ready once escaping fixed
4. ⏳ **Create test-video-stitching.ps1** → Integration test suite
5. ⏳ **Update docs** → Clarify error handling and verification steps

**Verdict:** Scripts are functional but **NOT production-ready** without path escaping and validation fixes.
