// Leaflet JS Interop for Offline Map Visual location background mode
(function () {
    const maps = {};
    let libLoaded = false;
    let libLoading = false;
    let libCallbacks = [];

    function loadLeaflet() {
        if (libLoaded) return Promise.resolve();
        if (libLoading) return new Promise(r => libCallbacks.push(r));
        libLoading = true;
        return new Promise((resolve, reject) => {
            libCallbacks.push(resolve);
            // Load CSS
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = '/lib/leaflet/leaflet.css';
            document.head.appendChild(link);
            // Load JS
            const script = document.createElement('script');
            script.src = '/lib/leaflet/leaflet.js';
            script.onload = () => {
                libLoaded = true;
                libLoading = false;
                libCallbacks.forEach(cb => cb());
                libCallbacks = [];
            };
            script.onerror = () => reject(new Error('Failed to load Leaflet'));
            document.head.appendChild(script);
        });
    }

    window.leafletInterop = {
        initMap: async function (containerId, dotNetRef, tileUrl, center, zoom, bounds, minZoom, maxZoom) {
            await loadLeaflet();

            const container = document.getElementById(containerId);
            if (!container) return;

            const mapOptions = {};

            if (bounds && bounds.north != null && bounds.south != null && bounds.east != null && bounds.west != null) {
                mapOptions.maxBounds = [
                    [bounds.south, bounds.west],
                    [bounds.north, bounds.east]
                ];
                mapOptions.maxBoundsViscosity = 1.0;
            }

            if (minZoom != null) mapOptions.minZoom = minZoom;
            if (maxZoom != null) mapOptions.maxZoom = maxZoom;

            const map = L.map(container, mapOptions).setView([center.lat, center.lng], zoom || 15);

            L.tileLayer(tileUrl || '/tiles/{z}/{x}/{y}.png', {
                maxZoom: maxZoom || 19,
                attribution: '&copy; OpenStreetMap contributors'
            }).addTo(map);

            maps[containerId] = { map: map, markers: [], dotNetRef: dotNetRef };
        },

        setMarkers: function (containerId, markersArray) {
            const state = maps[containerId];
            if (!state) return;

            // Clear existing markers
            state.markers.forEach(m => state.map.removeLayer(m));
            state.markers = [];

            markersArray.forEach(item => {
                const markerOptions = {};
                if (item.iconUrl) {
                    markerOptions.icon = L.icon({
                        iconUrl: item.iconUrl,
                        iconSize: [32, 32],
                        iconAnchor: [16, 32],
                        popupAnchor: [0, -32]
                    });
                }

                const marker = L.marker([item.lat, item.lng], markerOptions).addTo(state.map);

                if (item.label) {
                    marker.bindTooltip(item.label, {
                        direction: 'top',
                        offset: [0, -20],
                        className: 'leaflet-tooltip-custom'
                    });
                }

                marker.on('click', () => {
                    if (state.dotNetRef) {
                        state.dotNetRef.invokeMethodAsync('OnMarkerClicked', item.elementId);
                    }
                });

                state.markers.push(marker);
            });
        },

        dispose: function (containerId) {
            const state = maps[containerId];
            if (!state) return;
            state.markers.forEach(m => state.map.removeLayer(m));
            state.map.remove();
            if (state.dotNetRef) {
                state.dotNetRef.dispose();
            }
            delete maps[containerId];
        }
    };
})();
