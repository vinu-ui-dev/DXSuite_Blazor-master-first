window.panoramaInterop = {
    getAvailableHeight: function (element) {
        if (!element) return 0;
        var rect = element.getBoundingClientRect();
        return window.innerHeight - rect.top;
    }
};
