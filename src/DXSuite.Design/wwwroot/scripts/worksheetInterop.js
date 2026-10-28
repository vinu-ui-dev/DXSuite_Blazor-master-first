// Worksheet interop — manages the sticky summary header's stuck/expanded state.
//
// The header uses position:fixed (not sticky) when stuck. A spacer div holds its
// space in the document flow so content below never shifts.
//
// Going DOWN: spacer appears at full height → header goes fixed → groups animate closed.
//   Content below is unaffected because the spacer fills the vacated space throughout.
//
// Going UP: groups animate open while header is still fixed → THEN header returns to
//   normal flow and spacer disappears simultaneously. At that moment the header is
//   already full height, so swapping spacer→header is a zero-net-change.
//
// No Blazor StateHasChanged during scroll — all state is CSS classes toggled directly
// to avoid the re-render → layout-shift → scroll-anchoring feedback loop.
window.worksheetInterop = {
    _observer:      null,
    _restoreTimer:  null,

    initScrollWatcher: function () {
        const header   = document.getElementById('wsh-header');
        const sentinel = document.getElementById('wsh-sentinel');
        const spacer   = document.getElementById('wsh-spacer');
        if (!header || !sentinel || !spacer) return;

        // Capture the full expanded height once, before any collapse runs.
        const fullHeight = header.offsetHeight;

        this._observer = new IntersectionObserver(
            ([entry]) => {
                if (!entry.isIntersecting) {
                    // ── Scrolling DOWN: header is about to leave natural position ──
                    // Cancel any in-progress restore so we don't accidentally
                    // return to flow mid-animation.
                    if (this._restoreTimer) {
                        clearTimeout(this._restoreTimer);
                        this._restoreTimer = null;
                        header.classList.remove('wsh-is-restoring');
                    }
                    // 1. Lock spacer at full height BEFORE going fixed — content never shifts.
                    spacer.style.height  = fullHeight + 'px';
                    spacer.style.display = 'block';
                    // 2. Float the header; groups will animate closed via CSS.
                    header.classList.add('wsh-is-stuck');
                    header.classList.remove('wsh-user-expanded');

                } else {
                    // ── Scrolling UP: sentinel is back in view ──
                    // Step 1: expand the groups while still fixed (wsh-is-restoring
                    // overrides the max-height:0 from wsh-is-stuck).
                    header.classList.add('wsh-is-restoring');
                    header.classList.remove('wsh-user-expanded');

                    // Step 2: after the expand animation completes, atomically switch
                    // the header from fixed back to normal flow and remove the spacer.
                    // At this point the header is already at fullHeight, so the swap
                    // is invisible to the document — zero net change in occupied space.
                    this._restoreTimer = setTimeout(() => {
                        this._restoreTimer = null;
                        header.classList.remove('wsh-is-stuck', 'wsh-is-restoring', 'wsh-user-expanded');
                        spacer.style.display = 'none';
                    }, 250); // 250ms > 220ms CSS transition, giving it a little breathing room
                }
            },
            // rootMargin shrinks the effective viewport by the navbar height (3.5rem = 56px
            // at 16px base) so the observer fires exactly when the header hits top:3.5rem —
            // matching the moment position:fixed would activate.
            { threshold: 0, rootMargin: '-56px 0px 0px 0px' }
        );

        this._observer.observe(sentinel);
    },

    toggleDetails: function () {
        const header = document.getElementById('wsh-header');
        if (header) header.classList.toggle('wsh-user-expanded');
    },

    disposeScrollWatcher: function () {
        if (this._restoreTimer) {
            clearTimeout(this._restoreTimer);
            this._restoreTimer = null;
        }
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
    }
};
