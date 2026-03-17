mergeInto(LibraryManager.library, {

    /**
     * Returns true if the browser is running on an XR headset.
     * Checks both navigator.xr availability AND known headset user agents
     * (Meta Quest browser, Pico browser, etc).
     *
     * This catches Quest/Pico browsers that support WebXR even before
     * a session is requested.
     */
    WebGL_IsXRHeadset: function () {
        var ua = navigator.userAgent || '';

        // Known XR headset browsers identify themselves in the UA string
        var headsetPatterns = /OculusBrowser|Quest|Pico|VR/i;
        if (headsetPatterns.test(ua)) {
            return true;
        }

        // Fallback: check if WebXR immersive-vr is likely supported.
        // navigator.xr exists on many desktop browsers too, so we only
        // trust it combined with a mobile-class GPU or touch screen,
        // which desktop Chrome/Firefox/Edge won't have together with xr.
        if (navigator.xr && /Android/i.test(ua)) {
            return true;
        }

        return false;
    },

    /**
     * Returns true if the browser is on a mobile device (phone/tablet)
     * that is NOT an XR headset. Call WebGL_IsXRHeadset first.
     */
    WebGL_IsMobileDevice: function () {
        var ua = navigator.userAgent || '';
        return /Android|iPhone|iPad|iPod|Mobile|Tablet/i.test(ua);
    }

});
