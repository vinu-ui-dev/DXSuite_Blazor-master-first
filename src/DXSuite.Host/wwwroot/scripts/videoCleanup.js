window.unloadUploaderVideo = () => {
    const video = document.querySelector(".modal video");
    if (video) {
        video.pause();
        video.removeAttribute("src");
        video.load();
        console.log("Uploader video unloaded.");
    }
};
