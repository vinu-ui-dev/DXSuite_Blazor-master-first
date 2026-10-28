// Sample bundle exercising the full dx-location.js SDK surface. Use it as the
// reference for how a custom location talks to DXSuite.
(async function () {
    const dx = await DX.init();
    const settings = dx.context.settings || {};

    // Settings from the generic Configure modal (typed via manifest settingsSchema)
    if ((settings.theme && settings.theme) === 'dark') document.body.classList.add('dark');
    document.getElementById('welcome').textContent =
        (settings.welcomeText || 'Welcome') + ' — ' + dx.context.location.locationName;
    document.getElementById('context-line').textContent =
        `Signed in as ${dx.context.user.givenName || dx.context.user.userPrincipalName} · ` +
        `access: ${dx.context.access.level} · bundle v${dx.version}`;

    const canWrite = dx.context.access.level !== 'view';

    // Assets (visibility and per-item canEdit/canDelete are enforced server-side)
    if (settings.showAssets !== false) {
        const section = document.getElementById('assets-section');
        section.hidden = false;
        document.getElementById('upload-label').hidden = !canWrite;

        async function renderAssets() {
            const assets = await dx.api.getAssets();
            const container = document.getElementById('assets');
            container.innerHTML = '';
            for (const asset of assets) {
                const card = document.createElement('div');
                card.className = 'asset-card';
                card.dataset.assetId = asset.assetId;
                card.innerHTML = asset.assetTypeId === 0
                    ? `<img src="${asset.assetPath}" alt="" />`
                    : `<div class="placeholder">video</div>`;
                const label = document.createElement('div');
                label.textContent = asset.assetName || asset.assetPath.split('/').pop();
                card.appendChild(label);

                if (asset.canDelete) {
                    const del = document.createElement('button');
                    del.textContent = 'Delete';
                    del.onclick = async () => { await dx.api.deleteAsset(asset.assetId); await renderAssets(); };
                    card.appendChild(del);
                }
                container.appendChild(card);
            }
        }
        await renderAssets();

        document.getElementById('upload').addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const result = await dx.api.uploadAsset(file);
            if (result.approvalStatus === 0) alert('Uploaded — pending approval.');
            await renderAssets();
        });
    }

    // Displays + first display's schedule (read; write via dx.api.saveSchedule)
    const displayData = await dx.api.getDisplays();
    if (displayData.displays.length > 0) {
        document.getElementById('displays-section').hidden = false;
        const list = document.getElementById('displays');
        for (const display of displayData.displays) {
            const item = document.createElement('li');
            item.textContent = `${display.displayName} (${display.displayWidth}x${display.displayHeight})`;
            const schedule = await dx.api.getSchedule(display.displayId);
            item.textContent += ` — ${schedule.length} schedule event(s)`;
            list.appendChild(item);
        }
    }

    // Per-location storage
    const notes = document.getElementById('notes');
    try { notes.value = (await dx.storage.get('notes')) || ''; } catch { /* not set yet */ }
    document.getElementById('save-notes').onclick = async () => {
        await dx.storage.set('notes', notes.value);
        alert('Saved.');
    };

    // Host navigation + override-section integration
    document.getElementById('go-dashboard').onclick = () => dx.navigate('/director');
    dx.onOverrideAssetSelected((assetId) => {
        document.getElementById('override-info').textContent =
            assetId == null ? '' : `Override asset selected: ${assetId}`;
        document.querySelectorAll('.asset-card').forEach((card) =>
            card.classList.toggle('selected', card.dataset.assetId == assetId));
    });

    // Auto-refresh per settings
    const refreshSeconds = Number(settings.refreshSeconds) || 0;
    if (refreshSeconds > 0) setTimeout(() => window.location.reload(), refreshSeconds * 1000);

    // Fit the host iframe to our content
    dx.resize(document.body.scrollHeight + 40);
})();
