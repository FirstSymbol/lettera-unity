// Browsers expose no font files, so emoji are drawn by the browser's own canvas.
mergeInto(LibraryManager.library, {
    LetteraEmoji_Measure: function (text, pixels) {
        var state = Module.letteraEmoji;
        if (!state) {
            var canvas = document.createElement("canvas");
            state = Module.letteraEmoji = { canvas: canvas, context: canvas.getContext("2d", { willReadFrequently: true }) };
        }
        var context = state.context;
        context.font = pixels + 'px "Apple Color Emoji","Segoe UI Emoji","Noto Color Emoji",sans-serif';
        return context.measureText(UTF8ToString(text)).width;
    },

    // Writes width * height RGBA pixels, top row first, with straight alpha. The baseline is at baselineY.
    LetteraEmoji_Render: function (text, pixels, width, height, baselineY, output) {
        var state = Module.letteraEmoji;
        var canvas = state.canvas;
        // Setting the size clears the canvas and resets the context state.
        canvas.width = width;
        canvas.height = height;
        var context = state.context;
        context.font = pixels + 'px "Apple Color Emoji","Segoe UI Emoji","Noto Color Emoji",sans-serif';
        context.textBaseline = "alphabetic";
        // Only pictures that fall back to plain letters use this, such as flags on some systems.
        context.fillStyle = "#fff";
        context.fillText(UTF8ToString(text), 0, baselineY);
        HEAPU8.set(context.getImageData(0, 0, width, height).data, output);
    }
});
