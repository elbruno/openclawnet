# Decision: Video Production Pipeline — Root-Level Workspace and Enhanced Workflow

**Date:** 2026-05-09
**Author:** Milchick (Educational Media Producer)
**Status:** Approved and Implemented
**Approved By:** Bruno Capuano (via Mark - Lead Architect)

## Context

Video 1 (Skill-Powered Chat Journey) production pipeline required four improvements:
1. Fix idle/blank frame timing after welcome screen
2. Relocate video production assets to dedicated root-level workspace
3. Improve title card with OpenClawNet branding
4. Add optional audio narration and caption support

The original pipeline stored assets under `docs\testing\video-production`, mixed test documentation with production assets, and lacked narration support.

## Decision

**Workspace Structure:**
- Move all video production assets to `video-production\` at repository root
- Separate scenarios, scripts, and documentation into dedicated subdirectories
- Keep raw recordings, source assets, and final outputs organized per scenario

**Enhanced Stitching Pipeline:**
- Increase trim point from 7s to 20s based on frame content analysis
- Branded title card with OpenClawNet colors (#10213D navy, white text, #D8E6FF accents)
- Optional WAV narration input with automatic audio mixing
- Optional SRT captions with burn-in support (no external player requirements)
- Session-local ffmpeg detection for npm-installed binaries

**Implementation Approach:**
- Text-only title card using ffmpeg `drawtext` filter for cross-platform reliability
- Literal color values in filter strings to avoid PowerShell variable expansion issues
- Temporary files in output directory, cleaned up on success, preserved on error

## Alternatives Considered

1. **Keep assets under docs\testing\:**
   - Rejected: Video production is not "testing" — it's educational media production
   - Mixing test docs with production assets creates confusion
   - Root-level workspace clearly signals production status

2. **Logo overlay using ffmpeg movie filter:**
   - Attempted but failed silently (0-byte output)
   - Rejected in favor of text-only approach for reliability
   - Can be revisited with two-pass approach or PNG overlay filter

3. **Separate subtitle track instead of burned-in captions:**
   - Rejected: Requires player subtitle support
   - Burned-in captions work universally
   - SRT source remains editable for timing adjustments

4. **Cloud-based narration/TTS:**
   - Rejected: Adds external dependency
   - WAV input allows local recording or any TTS tool
   - Keeps pipeline reproducible without cloud credentials

## Consequences

**Positive:**
- Clear separation of video production from test documentation
- Reproducible pipeline with optional narration enhancement
- Branded title card aligns with OpenClawNet visual identity
- 20-second trim eliminates all idle/loading frames from final output
- Session-local ffmpeg support improves developer experience

**Negative:**
- Text-only title card lacks logo visual (acceptable tradeoff for reliability)
- Burned-in captions cannot be toggled off (acceptable for demo videos)
- Narration requires manual recording or external TTS tool

**Neutral:**
- Existing scenarios under docs\testing\ remain until explicitly migrated
- Old scripts\video-production\ scripts remain for backward compatibility during transition
- Team must update recording commands to new OPENCLAW_PLAYWRIGHT_VIDEO_DIR paths

## Migration Notes

- New structure: `video-production\scenarios\video-1-skill-journey\`
- Old structure: `docs\testing\video-production\scenarios\video-1-skill-journey\`
- Recording command updated in scenario README.md
- Stitching script relocated to `video-production\scripts\stitch-video-1-skill-journey.ps1`

## Validation

- ✓ Stitching script generates 33-second final MP4 with burned-in captions
- ✓ Title card displays correct branding and text hierarchy
- ✓ 20-second trim removes all idle frames (validated via frame extraction)
- ✓ Temporary files cleaned up on success
- ✓ git diff --check passes
- ✓ Session-local ffmpeg detection works with npm-installed binaries

## Related Files

- `video-production\README.md` — Workspace documentation
- `video-production\scripts\README.md` — Stitching script documentation
- `video-production\scripts\stitch-video-1-skill-journey.ps1` — Enhanced stitching script
- `video-production\scenarios\video-1-skill-journey\README.md` — Updated scenario docs
- `video-production\scenarios\video-1-skill-journey\narration\` — Narration scripts and SRT
- `.squad\agents\milchick\history.md` — Session learnings

## Future Enhancements

1. Logo overlay implementation if ffmpeg rendering becomes reliable across platforms
2. Automated frame content detection for optimal trim point selection
3. Narration recording workflow documentation if team adopts audio narration
4. Additional scenarios (Video 2: Deletion lifecycle, Video 3: Concurrency) migration to new structure
