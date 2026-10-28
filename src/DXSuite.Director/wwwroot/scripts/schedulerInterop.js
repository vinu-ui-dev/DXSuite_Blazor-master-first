// schedulerInterop.js — drag/resize/click interop for CounterCalendar.razor
// All drag feedback is handled 100% in JS; Blazor is called only once on drop/release
// to avoid SignalR round-trip latency during pointer moves.
//
// Grid orientation: time runs left-to-right (X axis), counters are rows (Y axis).
// 1 px = 1 minute, so 60 px = 1 hour, 1440 px = full day.

window.schedulerInterop = (function () {
    const SNAP_PX     = 15;   // 15 px = 15 minutes
    const PX_PER_MIN  = 1;
    const MIN_DURATION = 15;  // minimum event width in minutes

    // WeakMap guards prevent double-registration when Blazor re-renders
    const _registeredEvents    = new WeakMap();
    const _registeredResizes   = new WeakMap();
    const _registeredRows      = new WeakMap();
    const _registeredContainers = new WeakMap();

    function snapToGrid(px) {
        return Math.round(px / SNAP_PX) * SNAP_PX;
    }

    function clamp(val, min, max) {
        return Math.max(min, Math.min(max, val));
    }

    // ── Public API ───────────────────────────────────────────────────────────

    function init(dotNetRef, container) {
        if (!container || _registeredContainers.has(container)) return;
        _registeredContainers.set(container, true);

        container.querySelectorAll('[data-event-id]').forEach(el => {
            _registerEventDrag(dotNetRef, el);
            const handle = el.querySelector('[data-resize-handle]');
            if (handle) _registerResizeHandle(dotNetRef, handle, el);
        });

        container.querySelectorAll('[data-row-display-id]').forEach(row => {
            _registerRowCreate(dotNetRef, row);
        });
    }

    // Re-initialise after Blazor re-renders (new events added, container stayed).
    function refresh(dotNetRef, container) {
        if (!container) return;

        container.querySelectorAll('[data-event-id]').forEach(el => {
            if (!_registeredEvents.has(el)) {
                _registerEventDrag(dotNetRef, el);
                const handle = el.querySelector('[data-resize-handle]');
                if (handle && !_registeredResizes.has(handle))
                    _registerResizeHandle(dotNetRef, handle, el);
            }
        });

        container.querySelectorAll('[data-row-display-id]').forEach(row => {
            if (!_registeredRows.has(row))
                _registerRowCreate(dotNetRef, row);
        });
    }

    function destroy(container) {
        _registeredContainers.delete(container);
    }

    // ── Event drag (move — horizontal) ──────────────────────────────────────

    function _registerEventDrag(dotNetRef, eventEl) {
        if (_registeredEvents.has(eventEl)) return;
        _registeredEvents.set(eventEl, true);

        eventEl.addEventListener('pointerdown', function (e) {
            // Ignore clicks on the resize handle
            if (e.target.dataset.resizeHandle !== undefined) return;
            e.stopPropagation();
            e.preventDefault();

            const eventId   = parseInt(eventEl.dataset.eventId, 10);
            const rowEl     = eventEl.closest('[data-row-display-id]');
            if (!rowEl) return;

            const startX    = e.clientX;
            const origLeft  = parseInt(eventEl.style.left,  10) || 0;
            const origWidth = parseInt(eventEl.style.width, 10) || MIN_DURATION;

            // Ghost clone for visual feedback
            const ghost = eventEl.cloneNode(true);
            ghost.style.opacity = '0.5';
            ghost.style.pointerEvents = 'none';
            ghost.style.position = 'absolute';
            ghost.style.zIndex = '999';
            ghost.style.height = eventEl.offsetHeight + 'px';
            rowEl.appendChild(ghost);

            eventEl.style.opacity = '0.3';

            function onMove(me) {
                const dx      = me.clientX - startX;
                const rawLeft = origLeft + dx;
                const snapped = snapToGrid(clamp(rawLeft, 0, 1440 - origWidth));
                ghost.style.left = snapped + 'px';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',  onUp);

                ghost.remove();
                eventEl.style.opacity = '';

                const dx         = ue.clientX - startX;
                const rawLeft    = origLeft + dx;
                const snappedLeft = snapToGrid(clamp(rawLeft, 0, 1440 - origWidth));
                const newStart   = snappedLeft; // minutes from midnight

                if (Math.abs(snappedLeft - origLeft) >= SNAP_PX) {
                    dotNetRef.invokeMethodAsync('OnEventMoved', eventId, newStart);
                } else {
                    // No movement — treat as click to open edit modal
                    dotNetRef.invokeMethodAsync('OnEventActivated', eventId);
                }
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    // ── Resize handle (right edge — horizontal) ──────────────────────────────

    function _registerResizeHandle(dotNetRef, resizeEl, eventEl) {
        if (_registeredResizes.has(resizeEl)) return;
        _registeredResizes.set(resizeEl, true);

        resizeEl.addEventListener('pointerdown', function (e) {
            e.stopPropagation();
            e.preventDefault();

            const eventId   = parseInt(eventEl.dataset.eventId, 10);
            const startX    = e.clientX;
            const origLeft  = parseInt(eventEl.style.left,  10) || 0;
            const origWidth = parseInt(eventEl.style.width, 10) || MIN_DURATION;

            function onMove(me) {
                const dx       = me.clientX - startX;
                const rawW     = origWidth + dx;
                const snapped  = snapToGrid(Math.max(MIN_DURATION, rawW));
                eventEl.style.width = snapped + 'px';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',  onUp);

                const dx       = ue.clientX - startX;
                const snappedW = snapToGrid(Math.max(MIN_DURATION, origWidth + dx));
                const newEnd   = origLeft + snappedW; // minutes from midnight

                dotNetRef.invokeMethodAsync('OnEventResized', eventId, newEnd);
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    // ── Row click / drag-to-create (horizontal) ──────────────────────────────

    function _registerRowCreate(dotNetRef, rowEl) {
        if (_registeredRows.has(rowEl)) return;
        _registeredRows.set(rowEl, true);

        const displayId = parseInt(rowEl.dataset.rowDisplayId, 10);

        rowEl.addEventListener('pointerdown', function (e) {
            // Only fire for direct hits on the row background (not on event blocks)
            if (e.target.dataset.eventId !== undefined) return;
            if (e.target.closest('[data-event-id]')) return;

            e.preventDefault();

            const rowRect  = rowEl.getBoundingClientRect();
            const relX     = e.clientX - rowRect.left + rowEl.scrollLeft;
            const startMin = snapToGrid(clamp(relX, 0, 1440 - SNAP_PX));

            let ghost  = null;
            let isDrag = false;

            function onMove(me) {
                const curX   = me.clientX - rowRect.left + rowEl.scrollLeft;
                const curMin = snapToGrid(clamp(curX, startMin + MIN_DURATION, 1440));
                const w      = curMin - startMin;

                if (!isDrag && Math.abs(me.clientX - e.clientX) > 5) {
                    isDrag = true;
                    ghost  = document.createElement('div');
                    ghost.style.cssText =
                        `position:absolute; top:4px; bottom:4px; left:${startMin}px;` +
                        `width:${w}px; background:rgba(0,113,188,0.35);` +
                        `border:1px dashed #0071bc; border-radius:3px; pointer-events:none; z-index:900;`;
                    rowEl.appendChild(ghost);
                }

                if (ghost) ghost.style.width = w + 'px';
            }

            function onUp(ue) {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup',  onUp);
                if (ghost) ghost.remove();

                const endX   = ue.clientX - rowRect.left + rowEl.scrollLeft;
                const endMin = snapToGrid(clamp(endX, startMin + MIN_DURATION, 1440));

                dotNetRef.invokeMethodAsync('OnSlotActivated', displayId, startMin,
                    isDrag ? endMin : startMin + 60);
            }

            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup',   onUp);
        });
    }

    return { init, refresh, destroy };
})();
