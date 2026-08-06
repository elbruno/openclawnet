# 2026-05-09: QwenTTS Evaluation Documented for Video 1 Audio

**Decided By:** Ricken (DevRel/Writer)  
**Decision Date:** 2026-05-09T10:48:55  
**Status:** ✓ Complete

## Decision

Captured `ElBruno.QwenTTS` as a documented evaluation candidate for the Video 1 (Skill-Powered Chat Journey) narration/audio generation pipeline. No implementation decision made; evaluation kept open for future POC work.

## What Was Done

1. **Created:** `video-production/scenarios/video-1-skill-journey/narration/AUDIO-GENERATION-CANDIDATES.md`
   - Documented ElBruno.QwenTTS package details, strengths, and tradeoffs
   - Included evaluation points: local .NET/ONNX inference, WAV output, large model downloads, optional dependency scope
   - Outlined next steps for future POC if team decides to pursue

2. **Updated:** `video-production/scenarios/video-1-skill-journey/PRODUCTION_NOTES.md`
   - Added "Audio/Narration Candidates" subsection in Next Steps
   - Cross-linked to new evaluation notes

## Rationale

- **Team Memory:** Captures Mark's user directive (via Bruno) in discoverable documentation
- **Non-Blocking:** Evaluation documented without committing to implementation or dependency
- **Future-Ready:** If chosen for POC, all technical context is available
- **Developer Experience:** Centralizes audio tool discussion in narration workflow docs

## Key Evaluation Points (Documented)

- **Implementation:** Local .NET/ONNX TTS (no Python inference time dependency)
- **Output:** WAV format (compatible with existing stitching pipeline)
- **Tradeoffs:** Large first-run model downloads (~5.5–10 GB), ONNX Runtime footprint, CI repeatability testing needed
- **Scope:** Explicitly marked as optional tool, not default dependency

## Reference Materials

- **User Directive:** `.squad/decisions/inbox/copilot-directive-2026-05-09T14-48-06Z-qwentts-audio-evaluation.md`
- **Audio Evaluation:** `video-production/scenarios/video-1-skill-journey/narration/AUDIO-GENERATION-CANDIDATES.md`
- **Production Context:** `video-production/scenarios/video-1-skill-journey/PRODUCTION_NOTES.md`

## No Implementation

- ✗ ElBruno.QwenTTS package NOT added as dependency
- ✗ No code changes to product or test infrastructure
- ✗ No default narration generation implemented
- ✓ Evaluation candidate documented and discoverable for future work

---

**Next Action:** If the team decides to run a POC, open a new decision with implementation details and reference this evaluation note.
