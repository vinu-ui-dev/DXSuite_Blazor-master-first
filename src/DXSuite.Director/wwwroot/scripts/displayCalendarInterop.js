// displayCalendarInterop.js — drag/resize/click interop for DisplayCalendar.razor
// All drag feedback is handled 100% in JS; Blazor is called only once on drop/release
// to avoid SignalR round-trip latency during pointer moves.
//
// Grid orientation (week view): time runs top-to-bottom (Y axis), days are columns
// (X axis). 1 px = 1 minute vertically, so 60 px = 1 hour, 1440 px = full day.

window.displayCalendarInterop = (function () {
    const SNAP_PX      = 15;   // 15 px = 15 minutes
    const MIN_DURATION = 15;   // minimum event height in minutes
    const DAY_MINUTES  = 1440;

    // WeakMap guards prevent double-registration when Blazor re-renders
    const _registeredEvents     = new WeakMap();
    const _registeredResizes    = new WeakMap();
    const _registeredCols       = new WeakMap();

    function snapToGrid(px) {
        return Math.round(px / SNAP_PX) * SNAP_PX;
    }

    function clamp(val, min, max) {
        return Math.max(min, Math.min(max, val));
    }

    // Resolve which day column a pointer position falls in; null outside the grid
    function columnAtPoint(container, clientX) {
        const cols = container.querySelectorAll('[data-day-col]');
        for (const col of cols) {
            const r = col.getBoundingClientRect();
            if (clientX >= r.left && clientX < r.right) return col;
        }
        return null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    function init(dotNetRef, container) {
        if (!container) return;
        refresh(dotNetRef, container);
    }

    // Re-initialise after Blazor re-renders (new events added, container stayed).
    function refresh(dotNetRef, container) {
        if (!container) return;

        container.querySelectorAll('[data-occ-event-id]').forEach(el => {
            if (!_registeredEvents.has(el)) {
                _registerEventDrag(dotNetRef, container, el);
            }
            const handle = el.querySelector('[data-resize-handle]');
            if (handle && !_registeredResizes.has(handle)) {
                _registerResizeHandle(dotNetRef, handle, el);
            }
        });

        container.querySelectorAll('[data-day-col]').forEach(col => {
            if (!_registeredCols.has(col)) {
                _registerColumnCreate(dotNetRef, col);
            }
        });
    }

    // ── Event drag (move — vertical within a column, horizontal across days) ─

    function _registerEventDrag(dotNetRef, container, eventEl) {
        _registeredEvents.set(eventEl, true);

        eventEl.addEventListener('pointerdown', function (e) {
            if (e.target.dataset.resizeHandle !== undefined) return;
            if (eventEl.dataset.readonlyOcc !== undefined) {
                // All-day and read-only chips open the editor on click only
                dotNetRef.invokeMethodAsync('OnOccurrenceActivated',
                    parseInt(eventEl.dataset.occEventId, 10),
                    parseInt(eventEl.closest('[data-day-col]')?.dataset.dayIndex ?? "-1", 10));
                return;
            }
            e.stopPropagation();
            e.preventDefault();

            const eventId    = parseInt(eventEl.dataset.occEventId, 10);
            const sourceCol  = eventEl.closest('[data-day-col]');
            if (!sourceCol) return;
            const sourceDay  = parseInt(sourceCol.dataset.dayIndex, 10);

            const startX     = e.clientX;
            const startY     = e.clientY;
            const origTop    = parseInt(eventEl.style.top,    10) || 0;
            const origHeight = parseInt(eventEl.style.height, 10) || MIN_DURATION;

            let ghost = null;
            let ghostCol = sourceCol;
            let isDrag = false;

            function ensureGhost(col) {
                if (ghost && ghostCol === col) return;
                if (ghost) ghost.remove();
                ghost = eventEl.cloneNode(true);
                ghost.style.opacity = '0.5';
                ghost.style.pointerEvents = 'none';
                ghost.style.position = 'absolute';
                ghost.style.left = '2px';
                ghost.style.right = '2px';
                ghost.style.width = 'auto';
                ghost.style.zIndex = '999';
                col.appendChild(ghost);
                ghostCol = col;
            }

            function onMove(me) {
                if (!isDrag && Math.abs(me.clientY - startY) <= 3 && Math.abs(me.clientX - startX) <= 3) return;
                isDrag = true;

                const col = columnAtPoint(container, me.clientX) || ghostCol;
                ensureGhost(col);

                const dy      = me.clientY - startY;
                const snapped = snapToGrid(clamp(origTop + dy, 0, DAY_MINUTES - origHeight));
                ghost.style.top = snapped + 'px';
                eventEl.style.opacity = '0.3';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',   onUp);

                const targetCol = ghost ? ghostCol : sourceCol;
                if (ghost) ghost.remove();
                eventEl.style.opacity = '';

                if (!isDrag) {
                    // No movement — treat as click to open edit modal
                    dotNetRef.invokeMethodAsync('OnOccurrenceActivated', eventId, sourceDay);
                    return;
                }

                const dy        = ue.clientY - startY;
                const newStart  = snapToGrid(clamp(origTop + dy, 0, DAY_MINUTES - origHeight));
                const targetDay = parseInt(targetCol.dataset.dayIndex, 10);

                if (targetDay !== sourceDay || Math.abs(newStart - origTop) >= SNAP_PX) {
                    dotNetRef.invokeMethodAsync('OnOccurrenceMoved', eventId, sourceDay, targetDay, newStart);
                } else {
                    dotNetRef.invokeMethodAsync('OnOccurrenceActivated', eventId, sourceDay);
                }
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    // ── Resize handle (bottom edge — vertical) ───────────────────────────────

    function _registerResizeHandle(dotNetRef, resizeEl, eventEl) {
        _registeredResizes.set(resizeEl, true);

        resizeEl.addEventListener('pointerdown', function (e) {
            e.stopPropagation();
            e.preventDefault();

            const eventId    = parseInt(eventEl.dataset.occEventId, 10);
            const sourceDay  = parseInt(eventEl.closest('[data-day-col]')?.dataset.dayIndex ?? "-1", 10);
            const startY     = e.clientY;
            const origTop    = parseInt(eventEl.style.top,    10) || 0;
            const origHeight = parseInt(eventEl.style.height, 10) || MIN_DURATION;

            function onMove(me) {
                const dy      = me.clientY - startY;
                const snapped = snapToGrid(clamp(origHeight + dy, MIN_DURATION, DAY_MINUTES - origTop));
                eventEl.style.height = snapped + 'px';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',  onUp);

                const dy       = ue.clientY - startY;
                const snappedH = snapToGrid(clamp(origHeight + dy, MIN_DURATION, DAY_MINUTES - origTop));
                const newEnd   = origTop + snappedH; // minutes from midnight

                // Restore the drag feedback: Blazor's diff won't touch the JS-mutated
                // height, so a cancelled modal would otherwise leave it stretched.
                // A saved resize re-renders the block at its new size anyway.
                eventEl.style.height = origHeight + 'px';

                dotNetRef.invokeMethodAsync('OnOccurrenceResized', eventId, sourceDay, newEnd);
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    // ── Column click / drag-to-create (vertical) ─────────────────────────────

    function _registerColumnCreate(dotNetRef, colEl) {
        _registeredCols.set(colEl, true);

        const dayIndex = parseInt(colEl.dataset.dayIndex, 10);

        colEl.addEventListener('pointerdown', function (e) {
            // Only fire for direct hits on the column background (not on event blocks)
            if (e.target.closest('[data-occ-event-id]')) return;

            e.preventDefault();

            const colRect  = colEl.getBoundingClientRect();
            const relY     = e.clientY - colRect.top;
            const startMin = snapToGrid(clamp(relY, 0, DAY_MINUTES - SNAP_PX));

            let ghost  = null;
            let isDrag = false;

            function onMove(me) {
                const curY   = me.clientY - colRect.top;
                const curMin = snapToGrid(clamp(curY, startMin + MIN_DURATION, DAY_MINUTES));
                const h      = curMin - startMin;

                if (!isDrag && Math.abs(me.clientY - e.clientY) > 5) {
                    isDrag = true;
                    ghost  = document.createElement('div');
                    ghost.style.cssText =
                        `position:absolute; left:2px; right:2px; top:${startMin}px;` +
                        `height:${h}px; background:rgba(0,113,188,0.35);` +
                        `border:1px dashed #0071bc; border-radius:3px; pointer-events:none; z-index:900;`;
                    colEl.appendChild(ghost);
                }

                if (ghost) ghost.style.height = h + 'px';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',  onUp);
                if (ghost) ghost.remove();

                const endY   = ue.clientY - colRect.top;
                const endMin = snapToGrid(clamp(endY, startMin + MIN_DURATION, DAY_MINUTES));

                dotNetRef.invokeMethodAsync('OnSlotCreated', dayIndex, startMin,
                    isDrag ? endMin : startMin + 60);
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    return { init, refresh };
})();
