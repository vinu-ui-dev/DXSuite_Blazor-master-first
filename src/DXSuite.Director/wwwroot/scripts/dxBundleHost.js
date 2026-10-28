// Host-side postMessage bridge for location bundle iframes (Location.razor).
// The iframe is same-origin, but messages are still origin-checked and tagged with
// a source marker so unrelated postMessage traffic is ignored.

let listener = null;

export function init(dotNetRef, iframeId) {
    dispose();
    listener = (event) => {
        if (event.origin !== window.location.origin) return;
        const data = event.data;
        if (!data || data.source !== 'dx-location') return;

        if (data.type === 'navigate' && typeof data.href === 'string') {
            dotNetRef.invokeMethodAsync('OnBundleNavigate', data.href);
        } else if (data.type === 'resize' && typeof data.height === 'number') {
            const frame = document.getElementById(iframeId);
            if (frame) {
                frame.style.minHeight = Math.max(200, Math.min(data.height, 20000)) + 'px';
            }
        }
    };
    window.addEventListener('message', listener);
}

export function pushOverrideAsset(iframeId, assetId) {
    const frame = document.getElementById(iframeId);
    if (frame && frame.contentWindow) {
        frame.contentWindow.postMessage(
            { source: 'dx-host', type: 'override-asset', assetId: assetId ?? null },
            window.location.origin);
    }
}

export function dispose() {
    if (listener) {
        window.removeEventListener('message', listener);
        listener = null;
    }
}
