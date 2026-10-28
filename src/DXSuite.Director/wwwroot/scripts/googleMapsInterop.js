// Google Maps JS Interop for Visual location background mode
(function () {
    const maps = {};
    let apiLoaded = false;
    let apiLoading = false;
    let apiCallbacks = [];

    function loadApi(apiKey) {
        if (apiLoaded) return Promise.resolve();
        if (apiLoading) return new Promise(r => apiCallbacks.push(r));
        apiLoading = true;
        return new Promise((resolve, reject) => {
            apiCallbacks.push(resolve);
            const script = document.createElement('script');
            script.src = `https://maps.googleapis.com/maps/api/js?key=${apiKey}&callback=__gmapsReady`;
            script.async = true;
            script.defer = true;
            script.onerror = () => reject(new Error('Failed to load Google Maps API'));
            window.__gmapsReady = () => {
                apiLoaded = true;
                apiLoading = false;
                apiCallbacks.forEach(cb => cb());
                apiCallbacks = [];
                delete window.__gmapsReady;
            };
            document.head.appendChild(script);
        });
    }

    window.googleMapsInterop = {
        fitToViewport: function (containerId) {
            const container = document.getElementById(containerId);
            if (!container) return;
            function fit() {
                const rect = container.getBoundingClientRect();
                const available = window.innerHeight - rect.top - 12;
                container.style.height = Math.max(300, available) + 'px';
            }
            requestAnimationFrame(() => requestAnimationFrame(fit));
            container._gmapResizeHandler = fit;
            window.addEventListener('resize', fit);
        },

        initMap: async function (containerId, dotNetRef, apiKey, center, zoom, style, bounds, minZoom, maxZoom) {
            await loadApi(apiKey);

            const container = document.getElementById(containerId);
            if (!container) return;

            const mapOptions = {
                center: { lat: center.lat, lng: center.lng },
                zoom: zoom || 15,
                mapTypeId: style || 'roadmap',
                disableDefaultUI: false,
                zoomControl: true,
                mapTypeControl: true,
                streetViewControl: false,
                fullscreenControl: true
            };

            if (bounds && bounds.north != null && bounds.south != null && bounds.east != null && bounds.west != null) {
                mapOptions.restriction = {
                    latLngBounds: {
                        north: bounds.north,
                        south: bounds.south,
                        east: bounds.east,
                        west: bounds.west
                    },
                    strictBounds: true
                };
            }

            if (minZoom != null) mapOptions.minZoom = minZoom;
            if (maxZoom != null) mapOptions.maxZoom = maxZoom;

            const map = new google.maps.Map(container, mapOptions);
            maps[containerId] = { map: map, markers: [], dotNetRef: dotNetRef };
        },

        setMarkers: function (containerId, markersArray) {
            const state = maps[containerId];
            if (!state) return;

            // Clear existing markers
            state.markers.forEach(m => m.setMap(null));
            state.markers = [];

            markersArray.forEach(item => {
                const marker = new google.maps.Marker({
                    position: { lat: item.lat, lng: item.lng },
                    map: state.map,
                    title: item.label || '',
                    icon: item.iconUrl ? {
                        url: item.iconUrl,
                        scaledSize: new google.maps.Size(32, 32)
                    } : undefined
                });

                if (item.label) {
                    const infoWindow = new google.maps.InfoWindow({ content: `<div style="font-family:BAHNSCHRIFT;font-size:12px;font-weight:600;">${item.label}</div>` });
                    marker.addListener('mouseover', () => infoWindow.open(state.map, marker));
                    marker.addListener('mouseout', () => infoWindow.close());
                }

                marker.addListener('click', () => {
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
            state.markers.forEach(m => m.setMap(null));
            if (state.dotNetRef) {
                state.dotNetRef.dispose();
            }
            const container = document.getElementById(containerId);
            if (container?._gmapResizeHandler) {
                window.removeEventListener('resize', container._gmapResizeHandler);
            }
            delete maps[containerId];
        }
    };
})();
