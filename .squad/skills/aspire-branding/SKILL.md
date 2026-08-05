# Skill: aspire-branding

**Confidence:** high
**Domain:** content, docs, slides, demos, scripts

## Rule

Always write **"Aspire"** — never **".NET Aspire"** — in all generated content.

This applies to:
- Slide decks and speaker scripts
- Demo markdown files
- Documentation and manuals
- Session guides and README files
- Code comments and test strings
- Squad decisions, history, and logs

## Examples

| ❌ Wrong | ✅ Correct |
|----------|-----------|
| Deploy with .NET Aspire | Deploy with Aspire |
| .NET Aspire is already running | Aspire is already running |
| What is .NET Aspire? | What is Aspire? |
| Start .NET Aspire services | Start Aspire services |

## Rationale

The project uses "Aspire" as the canonical short-form brand name throughout all content.
Using ".NET Aspire" is inconsistent and was corrected project-wide on 2026-05-27.

## When to Apply

Before finalising any generated text that mentions Aspire. Do a find-and-replace pass
for ".NET Aspire" → "Aspire" before writing files.
