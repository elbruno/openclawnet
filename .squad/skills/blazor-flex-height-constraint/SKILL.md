# SKILL: Blazor Flex Child Height Constraint

@extracted: 2026-04-27, helly, from Chat.razor layout debugging session  
@validated-by: helly (high), petey (medium)

## Pattern
To prevent a Blazor component from growing unbounded inside a flex container, use \min-height: 0\ on the flex child (or its parent chain) to allow \max-height\ to take effect.

## Problem
In a flex container with \overflow-hidden\ or \lex-grow-1\, child elements often ignore \max-height\ constraints because the flex algorithm computes content-based sizing before applying max-height.

## Solution
\\\css
/* Parent flex container */
.chat-layout {
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

/* Child component that needs height constraint */
.console-body-expanded {
    max-height: 40vh;  /* Or 300px, etc. */
    overflow-y: auto;
    min-height: 0;     /* ⬅ Critical: allows max-height to work in flex context */
}
\\\

## Why It Works
- Flex items default to \min-height: auto\, which computes to content size
- Setting \min-height: 0\ overrides this, allowing \max-height\ to constrain the element
- Combine with \overflow-y: auto\ to create scrollable bounded regions

## When to Use
- Blazor components in flex layouts (e.g., Chat page with AgentConsolePanel)
- Any scrollable panel inside a flex container
- Grid layouts with \r\ units (same principle applies with \min-height\)

## Real-World Example
**Context:** AgentConsolePanel on Chat page was growing unbounded despite \max-height: 300px\

**Fix:**
\\\css
.console-body-expanded {
    max-height: 40vh;
    overflow-y: auto;
    min-height: 0;  /* Added this */
}
\\\

## References
- Commit d0b5983 (Agent Activity Panel fix)
- MDN: [CSS Flexible Box Layout: min-height](https://developer.mozilla.org/en-US/docs/Web/CSS/min-height)
