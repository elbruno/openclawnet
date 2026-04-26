// Live-tail auto-scroll helper for the Agent Activity panel.
// Standard "tail -f" UX: stays glued to the bottom while new entries
// arrive, but if the user scrolls up to read older lines we PAUSE
// auto-scroll until they scroll back near the bottom (within 30px).
(function () {
    const TOLERANCE_PX = 30;
    const state = new Map();

    function attach(elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;
        if (state.has(elementId)) {
            // Re-attach: panel may have been collapsed/re-expanded.
            const existing = state.get(elementId);
            el.removeEventListener('scroll', existing.handler);
        }
        const entry = { stickToBottom: true, handler: null };
        entry.handler = function () {
            const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
            entry.stickToBottom = distance <= TOLERANCE_PX;
        };
        el.addEventListener('scroll', entry.handler, { passive: true });
        // Start glued to the bottom.
        el.scrollTop = el.scrollHeight;
        state.set(elementId, entry);
    }

    function scrollToBottom(elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;
        const entry = state.get(elementId);
        if (!entry || entry.stickToBottom) {
            el.scrollTop = el.scrollHeight;
        }
    }

    function detach(elementId) {
        const el = document.getElementById(elementId);
        const entry = state.get(elementId);
        if (el && entry && entry.handler) {
            el.removeEventListener('scroll', entry.handler);
        }
        state.delete(elementId);
    }

    window.activityTail = { attach, scrollToBottom, detach };
})();
