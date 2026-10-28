// DXSuite Location Bundle SDK.
//
// Load from a bundle's entry HTML with:
//   <script src="/_content/DXSuite.Director/scripts/dx-location.js"></script>
//
// The host injects ?dxLocation={id}&dxVersion={version} onto the iframe URL; the SDK
// reads them and exposes a typed wrapper over the Location Content API. All requests
// are same-origin, so the user's session cookie authenticates them automatically and
// the server re-checks location access on every call — nothing here is trusted.
//
// Usage:
//   const dx = await DX.init();
//   const displays = await dx.api.getDisplays();
(function () {
    'use strict';

    const params = new URLSearchParams(window.location.search);
    const locationId = parseInt(params.get('dxLocation'), 10);
    const version = params.get('dxVersion');
    const apiBase = '/api/location-content/' + locationId;

    const overrideCallbacks = [];
    const contextCallbacks = [];

    window.addEventListener('message', (event) => {
        if (event.origin !== window.location.origin) return;
        const data = event.data;
        if (!data || data.source !== 'dx-host') return;

        if (data.type === 'override-asset') {
            overrideCallbacks.forEach((cb) => cb(data.assetId));
        } else if (data.type === 'context-changed') {
            contextCallbacks.forEach((cb) => cb());
        }
    });

    function post(message) {
        window.parent.postMessage(Object.assign({ source: 'dx-location' }, message), window.location.origin);
    }

    async function request(path, init) {
        init = init || {};
        // X-DX-Bundle is required by the server on writes as CSRF defense-in-depth.
        const headers = Object.assign({ 'X-DX-Bundle': '1' }, init.headers || {});
        const response = await fetch(apiBase + path, Object.assign({}, init, {
            credentials: 'same-origin',
            headers: headers
        }));

        if (response.status === 409) {
            // The bundle version changed under this tab; reloading re-resolves it.
            window.location.reload();
            throw new Error('Bundle version changed; reloading.');
        }
        if (!response.ok) {
            const text = await response.text().catch(() => '');
            throw new Error('DX API ' + response.status + (text ? ': ' + text : ''));
        }
        if (response.status === 204) return null;

        const contentType = response.headers.get('content-type') || '';
        return contentType.includes('application/json') ? response.json() : response.text();
    }

    function jsonInit(method, value) {
        return {
            method: method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(value)
        };
    }

    const DX = {
        locationId: locationId,
        version: version,
        context: null,

        // Fetches the location context (access level, settings, user) and signals the
        // host that the bundle is ready. Returns DX for chaining.
        init: async function () {
            DX.context = await request('/context');
            post({ type: 'ready' });
            return DX;
        },

        api: {
            getDisplays: function () { return request('/displays'); },
            getAssets: function () { return request('/assets'); },
            getSchedule: function (displayId) { return request('/schedule/' + displayId); },
            saveSchedule: function (displayId, events) { return request('/schedule/' + displayId, jsonInit('PUT', events)); },
            // Upload identities for this user come from DX.context.access.upload.candidates; pass one's
            // uploadingAs as options.uploadingAs. A caller with partial access through more than one rule
            // must choose, or the upload is refused with a 400 that lists the candidates.
            getUploadCandidates: function () {
                const upload = DX.context && DX.context.access && DX.context.access.upload;
                return upload ? upload.candidates : [];
            },
            uploadAsset: function (file, options) {
                options = options || {};
                const form = new FormData();
                form.append('file', file, file.name);
                if (options.constraintId != null) form.append('constraintId', options.constraintId);
                if (options.uploadingAs != null) form.append('uploadingAs', options.uploadingAs);
                return request('/assets', { method: 'POST', body: form });
            },
            deleteAsset: function (assetId) { return request('/assets/' + assetId, { method: 'DELETE' }); }
        },

        // Per-location key-value storage — persist bundle state without schema changes.
        storage: {
            list: function () { return request('/storage'); },
            get: function (key) { return request('/storage/' + encodeURIComponent(key)); },
            set: function (key, value) { return request('/storage/' + encodeURIComponent(key), jsonInit('PUT', value)); },
            remove: function (key) { return request('/storage/' + encodeURIComponent(key), { method: 'DELETE' }); }
        },

        // Ask the host page to navigate (e.g. dx.navigate('/location/42') or '/schedule/7').
        navigate: function (href) { post({ type: 'navigate', href: href }); },

        // Ask the host to grow the iframe to fit content height (pixels).
        resize: function (height) { post({ type: 'resize', height: height }); },

        // Fires with the asset id (or null) when the user picks an asset in the host's
        // override section (Custom Display locations only).
        onOverrideAssetSelected: function (callback) { overrideCallbacks.push(callback); },

        onContextChanged: function (callback) { contextCallbacks.push(callback); },

        // Escape hatch: authenticated fetch against /api/location-content/{locationId}.
        raw: request
    };

    window.DX = DX;
})();
