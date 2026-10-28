// Design module interop — browser-side helpers with no worksheet coupling.
window.designInterop = {
    // Streams a server-generated file to the browser as a download.
    // contentStreamRef is a Blazor DotNetStreamReference.
    downloadFileFromStream: async function (fileName, contentStreamRef) {
        const arrayBuffer = await contentStreamRef.arrayBuffer();
        const blob = new Blob([arrayBuffer]);
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName ?? '';
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }
};
