// Three.js Interop for 3D Model Visual location background mode
// Uses CSS2DRenderer for HTML sprite hotspots that match the image-mode card style
(function () {
    const scenes = {};
    let libLoaded = false;
    let libLoading = false;
    let libCallbacks = [];

    // Three.js globals set after loading
    let THREE, OrbitControls, GLTFLoader, CSS2DRenderer, CSS2DObject;

    async function loadThreeJs() {
        if (libLoaded) return;
        if (libLoading) return new Promise(r => libCallbacks.push(r));
        libLoading = true;

        try {
            const threeModule = await import('three');
            THREE = threeModule;

            const [controlsMod, loaderMod, css2dMod] = await Promise.all([
                import('three/addons/controls/OrbitControls.js'),
                import('three/addons/loaders/GLTFLoader.js'),
                import('three/addons/renderers/CSS2DRenderer.js'),
            ]);
            OrbitControls = controlsMod.OrbitControls;
            GLTFLoader = loaderMod.GLTFLoader;
            CSS2DRenderer = css2dMod.CSS2DRenderer;
            CSS2DObject = css2dMod.CSS2DObject;

            libLoaded = true;
            libLoading = false;
            libCallbacks.forEach(cb => cb());
            libCallbacks = [];
        } catch (err) {
            libLoading = false;
            throw err;
        }
    }

    // SVG icons matching the image-mode FloorPlan.razor cards
    const DISPLAY_SVG = '<svg viewBox="0 0 24 24" fill="#0071BC" style="width:28px;height:28px;"><path d="M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h7v2H8v2h8v-2h-2v-2h7c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z"/></svg>';
    const LOCATION_SVG = '<svg viewBox="0 0 24 24" fill="#0071BC" style="width:28px;height:28px;"><path d="M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z"/></svg>';

    function createHotspotElement(item, dotNetRef, scale) {
        const outer = document.createElement('div');
        outer.style.pointerEvents = 'none';

        const wrapper = document.createElement('div');
        wrapper.className = 'three-hotspot ' + (item.type === 'display' ? 'three-hotspot-display' : 'three-hotspot-location');
        wrapper.setAttribute('data-tooltip', item.label || '');
        wrapper.style.pointerEvents = 'auto';
        if (scale && scale !== 1) {
            wrapper.style.transform = 'scale(' + scale + ')';
            wrapper.style.transformOrigin = 'center bottom';
        }

        // Icon (custom image or default SVG)
        if (item.iconUrl) {
            const img = document.createElement('img');
            img.src = item.iconUrl;
            img.className = 'three-hotspot-icon';
            img.alt = item.label || '';
            wrapper.appendChild(img);
        } else {
            const iconDiv = document.createElement('div');
            iconDiv.innerHTML = item.type === 'display' ? DISPLAY_SVG : LOCATION_SVG;
            wrapper.appendChild(iconDiv);
        }

        // Label
        if (item.label) {
            const labelDiv = document.createElement('div');
            labelDiv.className = 'three-hotspot-label';
            labelDiv.textContent = item.label;
            wrapper.appendChild(labelDiv);
        }

        // Click handler
        let downTime = 0;
        wrapper.addEventListener('pointerdown', (e) => {
            downTime = Date.now();
            e.stopPropagation();
        });
        wrapper.addEventListener('pointerup', (e) => {
            e.stopPropagation();
            if (Date.now() - downTime < 300 && dotNetRef) {
                dotNetRef.invokeMethodAsync('OnHotspotClicked', item.elementId);
            }
        });

        outer.appendChild(wrapper);
        return outer;
    }

    window.threeDInterop = {
        initScene: async function (containerId, dotNetRef, modelUrl, cameraJson) {
            await loadThreeJs();

            const container = document.getElementById(containerId);
            if (!container) return;

            const width = container.clientWidth;
            const height = container.clientHeight || 500;

            const scene = new THREE.Scene();
            scene.background = new THREE.Color(0xf5f5f5);

            const camera = new THREE.PerspectiveCamera(60, width / height, 0.1, 1000);
            if (cameraJson) {
                try {
                    const cam = JSON.parse(cameraJson);
                    camera.position.set(cam.x || 5, cam.y || 5, cam.z || 5);
                    if (cam.rotX != null) camera.rotation.set(cam.rotX, cam.rotY || 0, cam.rotZ || 0);
                } catch {
                    camera.position.set(5, 5, 5);
                }
            } else {
                camera.position.set(5, 5, 5);
            }

            const renderer = new THREE.WebGLRenderer({ antialias: true });
            renderer.setSize(width, height);
            renderer.setPixelRatio(window.devicePixelRatio);
            renderer.shadowMap.enabled = true;
            container.appendChild(renderer.domElement);

            const labelRenderer = new CSS2DRenderer();
            labelRenderer.setSize(width, height);
            labelRenderer.domElement.style.position = 'absolute';
            labelRenderer.domElement.style.top = '0';
            labelRenderer.domElement.style.left = '0';
            labelRenderer.domElement.style.pointerEvents = 'auto';
            labelRenderer.domElement.style.zIndex = '1';
            container.appendChild(labelRenderer.domElement);

            const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
            scene.add(ambientLight);
            const dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
            dirLight.position.set(10, 20, 10);
            dirLight.castShadow = true;
            scene.add(dirLight);

            const controls = new OrbitControls(camera, labelRenderer.domElement);
            controls.enableDamping = true;
            controls.dampingFactor = 0.05;

            const state = {
                scene, camera, renderer, labelRenderer, controls,
                hotspots: [],
                dotNetRef, animating: true
            };
            scenes[containerId] = state;

            if (modelUrl) {
                const loader = new GLTFLoader();
                loader.load(modelUrl, (gltf) => {
                    scene.add(gltf.scene);
                    const box = new THREE.Box3().setFromObject(gltf.scene);
                    const center = box.getCenter(new THREE.Vector3());
                    const size = box.getSize(new THREE.Vector3());
                    const maxDim = Math.max(size.x, size.y, size.z);
                    if (!cameraJson) {
                        camera.position.set(center.x + maxDim, center.y + maxDim * 0.7, center.z + maxDim);
                    }
                    controls.target.copy(center);
                    controls.update();
                }, undefined, (error) => {
                    console.error('Error loading GLB model:', error);
                });
            }

            const onResize = () => {
                const rect = container.getBoundingClientRect();
                const available = window.innerHeight - rect.top - 12;
                container.style.height = Math.max(300, available) + 'px';
                const w = container.clientWidth;
                const h = container.clientHeight || 500;
                camera.aspect = w / h;
                camera.updateProjectionMatrix();
                renderer.setSize(w, h);
                labelRenderer.setSize(w, h);
            };
            window.addEventListener('resize', onResize);
            state._onResize = onResize;

            function animate() {
                if (!state.animating) return;
                requestAnimationFrame(animate);
                controls.update();
                renderer.render(scene, camera);
                labelRenderer.render(scene, camera);
            }
            animate();
        },

        setHotspots: function (containerId, hotspotsArray, scale) {
            const state = scenes[containerId];
            if (!state) return;

            state.hotspots.forEach(h => state.scene.remove(h.css2d));
            state.hotspots = [];

            const s = scale || 1;
            hotspotsArray.forEach(item => {
                const el = createHotspotElement(item, state.dotNetRef, s);
                const css2d = new CSS2DObject(el);
                css2d.position.set(item.x, item.y, item.z);
                state.scene.add(css2d);
                state.hotspots.push({ css2d });
            });
        },

        getCameraState: function (containerId) {
            const state = scenes[containerId];
            if (!state) return null;
            const pos = state.camera.position;
            const rot = state.camera.rotation;
            return JSON.stringify({
                x: pos.x, y: pos.y, z: pos.z,
                rotX: rot.x, rotY: rot.y, rotZ: rot.z
            });
        },

        fitToViewport: function (containerId) {
            const container = document.getElementById(containerId);
            if (!container) return;
            function fit() {
                const rect = container.getBoundingClientRect();
                const available = window.innerHeight - rect.top - 12;
                container.style.height = Math.max(300, available) + 'px';
            }
            requestAnimationFrame(() => requestAnimationFrame(fit));
        },

        dispose: function (containerId) {
            const state = scenes[containerId];
            if (!state) return;

            state.animating = false;
            window.removeEventListener('resize', state._onResize);

            state.hotspots.forEach(h => state.scene.remove(h.css2d));

            state.controls.dispose();
            state.renderer.dispose();

            const container = document.getElementById(containerId);
            if (container) {
                if (state.renderer.domElement.parentNode === container)
                    container.removeChild(state.renderer.domElement);
                if (state.labelRenderer.domElement.parentNode === container)
                    container.removeChild(state.labelRenderer.domElement);
            }

            if (state.dotNetRef) state.dotNetRef.dispose();
            delete scenes[containerId];
        }
    };
})();
