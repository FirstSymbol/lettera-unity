// Browsers expose no font files, so emoji are drawn by the browser's own canvas.
mergeInto(LibraryManager.library, {
    // A touch browser raises its keyboard only for an element focused inside a touch handler, and Unity runs a frame later.
    // So a press on a field arms this while the finger is down, and the handler here focuses a hidden text box when it lifts.
    LetteraCursor_Set: function (css) {
        var canvas = Module.canvas || document.querySelector("canvas");
        if (canvas) canvas.style.cursor = UTF8ToString(css);
    },

    // A browser opens a window only inside the user's own gesture, and Unity hears of a release a frame later.
    // So a press on a link leaves its address and places here, and the release decides in the page.
    LetteraLink_Arm: function (url, rects, count) {
        var state = Module.letteraLink;
        if (!state) {
            state = Module.letteraLink = { url: null, rects: [], opened: false };
            var release = function (e) {
                if (!state.url) return;
                var canvas = Module.canvas || document.querySelector("canvas");
                var point = e.changedTouches && e.changedTouches.length ? e.changedTouches[0] : e;
                var box = canvas.getBoundingClientRect();
                var x = (point.clientX - box.left) * canvas.width / box.width;
                var y = canvas.height - (point.clientY - box.top) * canvas.height / box.height;
                var inside = false;
                for (var i = 0; i + 3 < state.rects.length; i += 4)
                    inside = inside || (x >= state.rects[i] && y >= state.rects[i + 1] && x <= state.rects[i + 2] && y <= state.rects[i + 3]);
                var address = state.url;
                state.url = null;
                if (!inside) return;
                state.opened = true;
                window.open(address, "_blank", "noopener");
            };
            document.addEventListener("pointerup", release, true);
            document.addEventListener("touchend", release, true);
            document.addEventListener("mouseup", release, true);
        }
        state.url = UTF8ToString(url);
        state.opened = false;
        state.rects = [];
        for (var i = 0; i < count; i++) state.rects.push(HEAPF32[(rects >> 2) + i]);
    },

    LetteraLink_Disarm: function () {
        if (Module.letteraLink) Module.letteraLink.url = null;
    },

    LetteraLink_TakeOpened: function () {
        var state = Module.letteraLink;
        if (!state) return 0;
        var opened = state.opened;
        state.opened = false;
        state.url = null;
        return opened ? 1 : 0;
    },

    LetteraMotion_PrefersReduced: function () {
        return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 1 : 0;
    },

    LetteraInput_HasCoarsePointer: function () {
        return window.matchMedia && window.matchMedia("(pointer: coarse)").matches ? 1 : 0;
    },

    LetteraInput_Arm: function () {
        var state = Module.letteraInput;
        if (!state) {
            var box = document.createElement("textarea");
            // 16 px keeps iOS from zooming the page, and the box stays where the caret is for the candidate window.
            box.style.cssText = "position:fixed;left:0;top:0;width:2px;height:20px;opacity:0;border:0;padding:0;margin:0;outline:0;resize:none;overflow:hidden;font-size:16px;color:transparent;background:transparent;caret-color:transparent;z-index:-1;";
            box.setAttribute("autocapitalize", "off");
            box.setAttribute("aria-hidden", "true");
            box.spellcheck = false;
            document.body.appendChild(box);
            state = Module.letteraInput = { box: box, armed: false, flags: 0, composition: "", startX: 0, startY: 0, moved: false, open: false };
            box.addEventListener("input", function () { state.flags |= 1; });
            box.addEventListener("compositionstart", function () { state.composing = true; state.composition = ""; state.flags |= 2; });
            box.addEventListener("compositionupdate", function (e) { state.composition = e.data || ""; state.flags |= 2; });
            box.addEventListener("compositionend", function () { state.composing = false; state.composition = ""; state.flags |= 3; });
            box.addEventListener("keydown", function (e) {
                e.stopPropagation();
                if (e.key === "Enter" && !e.isComposing && !state.multiline) {
                    e.preventDefault();
                    state.flags |= 4;
                }
            });
            box.addEventListener("keypress", function (e) { e.stopPropagation(); });
            box.addEventListener("keyup", function (e) { e.stopPropagation(); });
            box.addEventListener("blur", function () {
                // A press on the page takes the focus for a moment; only staying away counts.
                setTimeout(function () { if (state.open && document.activeElement !== box) state.flags |= 8; }, 150);
            });
            document.addEventListener("touchstart", function (e) {
                var touch = e.touches[0];
                if (touch) { state.startX = touch.clientX; state.startY = touch.clientY; state.moved = false; }
            }, true);
            document.addEventListener("touchmove", function (e) {
                var touch = e.touches[0];
                if (touch && Math.abs(touch.clientX - state.startX) + Math.abs(touch.clientY - state.startY) > 10) state.moved = true;
            }, true);
            var lift = function () {
                if (state.armed && !state.moved) box.focus({ preventScroll: true });
                state.armed = false;
            };
            document.addEventListener("touchend", lift, true);
            document.addEventListener("mouseup", lift, true);
        }
        state.armed = true;
    },

    LetteraInput_Open: function (text, start, end, multiline, secure, kind, returnKey) {
        _LetteraInput_Arm();
        var state = Module.letteraInput;
        state.armed = false;
        state.open = true;
        state.multiline = !!multiline;
        state.flags = 0;
        var box = state.box;
        var modes = ["text", "text", "url", "numeric", "decimal", "tel", "text", "email", "search"];
        var keys = ["", "done", "go", "next", "search", "send"];
        box.setAttribute("inputmode", modes[kind] || "text");
        if (keys[returnKey]) box.setAttribute("enterkeyhint", keys[returnKey]);
        else box.removeAttribute("enterkeyhint");
        box.setAttribute("autocomplete", secure ? "off" : "on");
        box.value = UTF8ToString(text);
        box.setSelectionRange(start, end);
        box.focus({ preventScroll: true });
    },

    LetteraInput_Close: function () {
        var state = Module.letteraInput;
        if (!state) return;
        state.open = false;
        state.armed = false;
        state.flags = 0;
        state.composing = false;
        state.box.value = "";
        state.box.blur();
    },

    LetteraInput_TakeFlags: function () {
        var state = Module.letteraInput;
        if (!state) return 0;
        var flags = state.flags;
        state.flags = 0;
        return flags;
    },

    LetteraInput_IsComposing: function () {
        var state = Module.letteraInput;
        return state && state.composing ? 1 : 0;
    },

    LetteraInput_GetText: function (buffer, size) {
        var text = Module.letteraInput ? Module.letteraInput.box.value : "";
        if (buffer) stringToUTF8(text, buffer, size);
        return lengthBytesUTF8(text);
    },

    LetteraInput_GetComposition: function (buffer, size) {
        var text = Module.letteraInput ? Module.letteraInput.composition : "";
        if (buffer) stringToUTF8(text, buffer, size);
        return lengthBytesUTF8(text);
    },

    LetteraInput_GetCaret: function () {
        return Module.letteraInput ? Module.letteraInput.box.selectionEnd : 0;
    },

    LetteraInput_Set: function (text, start, end) {
        var state = Module.letteraInput;
        if (!state) return;
        var value = UTF8ToString(text);
        if (state.box.value !== value) state.box.value = value;
        state.box.setSelectionRange(start, end);
    },

    // Fractions of the canvas, from its bottom left.
    LetteraInput_Place: function (x, y) {
        var state = Module.letteraInput;
        if (!state || !Module.canvas) return;
        var rect = Module.canvas.getBoundingClientRect();
        state.box.style.left = (rect.left + rect.width * x) + "px";
        state.box.style.top = (rect.top + rect.height * (1 - y)) + "px";
    },

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
