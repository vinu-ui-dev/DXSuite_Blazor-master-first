// Triggers a file download by navigating an invisible link to a URL.
// The browser handles the Content-Disposition: attachment header from the
// server, so the user stays on the current page while the file saves.
window.dxDownloadUrl = function (url) {
    const link = document.createElement('a');
    link.href = url;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
