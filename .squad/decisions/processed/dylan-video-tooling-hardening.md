# Video Tooling Hardening Decision

**Author:** Dylan (Tester)  
**Date:** 2026-05-09  
**Status:** Implemented  

## Context

The Video 1 stitching script (`stitch-video-1-skill-journey.ps1`) had several reliability and robustness issues:
- Path handling broke when invoked from directories other than `video-production\scripts`
- FFmpeg concat demux file paths were not escaped for Windows paths with spaces/backslashes/single quotes
- Narration/caption ffmpeg invocation used `Invoke-Expression`, which is error-prone with path escaping
- Output validation only warned on short duration; didn't validate codec, resolution, fps, or pixel format
- Temp files were preserved on failure but location message was unclear

## Decision

Implemented five hardening improvements:

### 1. Script-Relative Path Resolution
- Added `$ScriptDir = Split-Path -Path $PSCommandPath -Parent` to determine script directory
- All default relative paths now resolve relative to script directory using `Join-Path $ScriptDir`
- Absolute paths pass through unchanged
- **Benefit:** Script works correctly when invoked from any working directory

### 2. Windows-Safe FFmpeg Concat Demux Paths
- Escape backslashes to forward slashes for concat demuxer
- Escape single quotes as `'\''` per FFmpeg concat spec
- Applied to all three video segments in concat file
- **Benefit:** Handles Windows paths with spaces, backslashes, and single quotes correctly

### 3. Argument Array for FFmpeg Invocation
- Replaced `Invoke-Expression` with argument array splatting (`@ffmpegArgs`)
- Build array with `-i`, `-map`, `-vf`, `-c:v`, etc. as discrete elements
- Use `& $ffmpeg @ffmpegArgs` for safe invocation
- **Benefit:** Eliminates quoting/escaping bugs in PowerShell string interpolation

### 4. Deterministic Output Validation
- Query ffprobe for codec, resolution, fps, pixel format using JSON output
- Validate:
  - Video codec: h264
  - Resolution: 1280x720
  - Frame rate: 29-31 fps (allowing for minor variance)
  - Pixel format: yuv420p
  - Duration: >= 20s
  - File size: > 0 bytes
- **Fail** (not warn) if validation fails
- **Benefit:** Catches invalid output immediately; prevents bad videos from being committed

### 5. Preserved Temp Files on Failure
- Temp files already preserved on failure (catch block)
- Improved error message to list temp files by name
- Cleanup on success unchanged
- **Benefit:** Easier debugging when script fails

## Alternatives Considered

### Alternative 1: Use Start-Process instead of argument arrays
- Rejected: More verbose, harder to read, no significant benefit over argument arrays

### Alternative 2: Validate only codec and resolution
- Rejected: FPS and pixel format are equally important for video quality/compatibility

### Alternative 3: Warn instead of fail on validation errors
- Rejected: Task explicitly required failing on invalid output

## Validation

Tested script successfully:
- From `video-production\scripts` directory
- From project root directory
- Output validated: h264, 1280x720, 30 fps, yuv420p, 33s duration
- Temp files cleaned up on success

## Impact

- **User Experience:** More reliable video generation with clear error messages
- **Maintenance:** Easier to debug failures with preserved temp files
- **Quality:** Deterministic validation prevents invalid outputs
- **Compatibility:** Works from any working directory

## Related Files

- `video-production\scripts\stitch-video-1-skill-journey.ps1` (modified)
- `video-production\scripts\README.md` (documented validation behavior)
- `video-production\scripts\setup-and-stitch.ps1` (no changes needed; already robust)
