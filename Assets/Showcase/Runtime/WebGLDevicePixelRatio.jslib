// Returns window.devicePixelRatio so C# can compute panel scale that
// compensates for the HiDPI canvas. The WebGL template's resize() sets
// canvas.width = innerWidth * devicePixelRatio so Unity renders into a
// HiDPI buffer for crisp text — but Screen.width then reports the BUFFER
// pixel count (e.g. 5120 on a 5K Mac), not the CSS pixel count (2560).
// A ConstantPixelSize panel without compensation therefore renders 36-px
// components at 18 CSS pixels on every Retina display.
mergeInto(LibraryManager.library, {
    LoLDS_GetDevicePixelRatio: function () {
        return window.devicePixelRatio || 1.0;
    },
});
