window.floorPlanPanZoom = (() => {
    const instances = {};

    function init(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const inner = canvas.querySelector('.fp-pan-zoom');
        if (!inner) return;

        // Size the canvas viewport to fill remaining screen space
        function fitToViewport() {
            const rect = canvas.getBoundingClientRect();
            const available = window.innerHeight - rect.top - 12;
            canvas.style.height = Math.max(200, available) + 'px';
        }
        // Defer initial measurement until layout is stable
        requestAnimationFrame(() => requestAnimationFrame(fitToViewport));
        window.addEventListener('resize', fitToViewport);

        const state = {
            scale: 1,
            tx: 0,
            ty: 0,
            minScale: 0.5,
            maxScale: 4,
            dragging: false,
            lastX: 0,
            lastY: 0,
            pointerId: null,
            onResize: fitToViewport
        };

        function applyTransform() {
            inner.style.transform = `translate(${state.tx}px, ${state.ty}px) scale(${state.scale})`;
        }

        function onWheel(e) {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const mx = e.clientX - rect.left;
            const my = e.clientY - rect.top;

            const oldScale = state.scale;
            const delta = e.deltaY > 0 ? 0.9 : 1.1;
            state.scale = Math.min(state.maxScale, Math.max(state.minScale, state.scale * delta));

            // Zoom toward cursor position
            const ratio = state.scale / oldScale;
            state.tx = mx - ratio * (mx - state.tx);
            state.ty = my - ratio * (my - state.ty);

            applyTransform();
        }

        function onPointerDown(e) {
            if (e.button !== 0) return;
            // Don't start pan if clicking an interactive element
            if (e.target.closest('.fp-display-element, .fp-location-element')) return;

            state.dragging = true;
            state.pointerId = e.pointerId;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
            canvas.setPointerCapture(e.pointerId);
            canvas.style.cursor = 'grabbing';
        }

        function onPointerMove(e) {
            if (!state.dragging || e.pointerId !== state.pointerId) return;
            state.tx += e.clientX - state.lastX;
            state.ty += e.clientY - state.lastY;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
            applyTransform();
        }

        function onPointerUp(e) {
            if (e.pointerId !== state.pointerId) return;
            state.dragging = false;
            state.pointerId = null;
            canvas.style.cursor = 'grab';
        }

        function reset() {
            state.scale = 1;
            state.tx = 0;
            state.ty = 0;
            applyTransform();
        }

        canvas.addEventListener('wheel', onWheel, { passive: false });
        canvas.addEventListener('pointerdown', onPointerDown);
        canvas.addEventListener('pointermove', onPointerMove);
        canvas.addEventListener('pointerup', onPointerUp);
        canvas.addEventListener('pointercancel', onPointerUp);
        canvas.addEventListener('dblclick', (e) => {
            if (e.target.closest('.fp-display-element, .fp-location-element')) return;
            reset();
        });

        canvas.style.cursor = 'grab';
        canvas.style.touchAction = 'none';
        applyTransform();

        instances[canvasId] = { state, canvas, inner, reset };
    }

    function dispose(canvasId) {
        const inst = instances[canvasId];
        if (inst) {
            window.removeEventListener('resize', inst.state.onResize);
        }
        delete instances[canvasId];
    }

    function resetView(canvasId) {
        instances[canvasId]?.reset();
    }

    function zoom(canvasId, factor) {
        const inst = instances[canvasId];
        if (!inst) return;
        const { state, canvas } = inst;
        const rect = canvas.getBoundingClientRect();
        // Zoom toward center of the canvas
        const mx = rect.width / 2;
        const my = rect.height / 2;
        const oldScale = state.scale;
        state.scale = Math.min(state.maxScale, Math.max(state.minScale, state.scale * factor));
        const ratio = state.scale / oldScale;
        state.tx = mx - ratio * (mx - state.tx);
        state.ty = my - ratio * (my - state.ty);
        inst.inner.style.transform = `translate(${state.tx}px, ${state.ty}px) scale(${state.scale})`;
    }

    return { init, dispose, resetView, zoom };
})();
