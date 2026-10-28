window.dxGetElementRect = (element) => {
    const r = element.getBoundingClientRect();
    return { bottom: r.bottom, left: r.left, width: r.width };
};

window.dxScrollElementTo = (element, top) => {
    if (element) element.scrollTop = top;
};

// Drag-to-resize for a dx-table column. The table element carries one CSS custom property per
// column and the header and rows size themselves from those, so a drag repaints by setting a
// single property rather than re-rendering every row through Blazor. Only the final width is
// sent back to .NET, on release.
//
// The header cell is draggable for column reordering, and a drag started on the handle would be
// picked up by that instead, so reordering is switched off for the duration of the resize.
window.dxStartColumnResize = (columnKey, startX, dotNetRef) => {
    const handle = document.querySelector(`[data-resize-col="${columnKey}"]`);
    const table = handle?.closest(".dx-table");
    if (!table) return;

    const cell = handle.parentElement;
    const varName = `--dxcol-${columnKey}`;
    const startWidth = parseFloat(getComputedStyle(table).getPropertyValue(varName)) || cell.offsetWidth;
    const minWidth = 48;
    let width = startWidth;

    const onMove = (e) => {
        width = Math.max(minWidth, Math.round(startWidth + (e.clientX - startX)));
        table.style.setProperty(varName, `${width}px`);
    };

    const onUp = () => {
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
        document.body.style.userSelect = "";
        cell.setAttribute("draggable", "true");
        dotNetRef.invokeMethodAsync("OnColumnResized", columnKey, `${width}px`);
    };

    cell.setAttribute("draggable", "false");
    document.body.style.userSelect = "none";
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
};

// Firefox refuses to initiate an HTML5 drag unless dataTransfer carries data,
// and Blazor @ondragstart handlers have no way to set it. Seed empty payload
// for any drag that starts without one.
document.addEventListener("dragstart", (e) => {
    if (e.dataTransfer && e.dataTransfer.types.length === 0) {
        e.dataTransfer.setData("text/plain", "");
    }
});

// Mobile autoplay rescue: Chromium ignores the muted content attribute on
// dynamically-inserted <video> elements, so strict mobile autoplay policy
// classifies them as audible and blocks them. Re-assert the muted property and
// retry play() for every autoplay video Blazor renders, now and in the future.
(function () {
    const nudge = (v) => {
        v.muted = true;
        if (v.autoplay && v.paused) {
            v.play().catch(() => { /* blocked (e.g. battery saver) — leave paused */ });
        }
    };
    const scan = (root) => {
        if (root.querySelectorAll) {
            root.querySelectorAll("video[autoplay]").forEach(nudge);
        }
    };
    new MutationObserver((mutations) => {
        for (const m of mutations) {
            for (const n of m.addedNodes) {
                if (n.nodeType !== 1) continue;
                if (n.tagName === "VIDEO") nudge(n);
                else scan(n);
            }
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    scan(document);
})();
