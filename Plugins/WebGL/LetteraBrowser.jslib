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
            box.addEventListener("input", function () { if (!state.host) state.flags |= 1; });
            box.addEventListener("compositionstart", function () { state.composing = true; state.composition = ""; state.flags |= 2; });
            box.addEventListener("compositionupdate", function (e) { state.composition = e.data || ""; state.flags |= 2; });
            box.addEventListener("compositionend", function () { state.composing = false; state.composition = ""; state.flags |= 3; });
            box.addEventListener("keydown", function (e) {
                e.stopPropagation();
                if (state.host) return;
                if (e.key === "Enter" && !e.isComposing && !state.multiline) {
                    e.preventDefault();
                    state.flags |= 4;
                }
            });
            box.addEventListener("keypress", function (e) { e.stopPropagation(); });
            box.addEventListener("keyup", function (e) { e.stopPropagation(); });
            box.addEventListener("blur", function () {
                // A press on the page takes the focus for a moment; only staying away counts.
                setTimeout(function () { if (state.open && !state.host && document.activeElement !== box) state.flags |= 8; }, 150);
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

    // The box a field's session runs through, on every web device. What happens in it becomes events; the field decides what they mean.
    LetteraHost_Ensure__deps: ["LetteraInput_Arm"],
    LetteraHost_Ensure: function () {
        _LetteraInput_Arm();
        var state = Module.letteraInput;
        state.armed = false;
        if (state.queue) return;
        state.queue = [];
        state.revision = 0;
        state.silent = 0;
        state.fields = [];
        var push = function (event) {
            event.revision = state.revision;
            state.queue.push(event);
        };
        // The keys that do not type go to the field's own rules, so word and grapheme steps are the same everywhere.
        var codes = { Backspace: 1, Delete: 2, Enter: 3, NumpadEnter: 4, Tab: 5, Escape: 6, ArrowLeft: 7, ArrowRight: 8, ArrowUp: 9, ArrowDown: 10, Home: 11, End: 12, PageUp: 13, PageDown: 14, Insert: 15, ContextMenu: 16, F10: 17 };
        var attach = function (box) {
            box.addEventListener("input", function () {
                if (!state.host || state.composing) return;
                push({ kind: 1, text: box.value, caret: box.selectionStart });
            });
            box.addEventListener("compositionstart", function () {
                if (!state.host) return;
                state.compositionFrom = box.selectionStart;
            });
            box.addEventListener("compositionupdate", function (e) {
                if (!state.host) return;
                var text = e.data || "";
                push({ kind: 2, text: text, caret: Math.max(0, box.selectionStart - state.compositionFrom), start: 0, end: 0 });
            });
            box.addEventListener("compositionend", function () {
                if (!state.host) return;
                // What it became arrives with the box's text, as one edit.
                push({ kind: 3, commit: 0 });
                push({ kind: 1, text: box.value, caret: box.selectionStart });
            });
            box.addEventListener("keydown", function (e) {
                if (!state.host) return;
                e.stopPropagation();
                if (e.isComposing) return;
                var chord = e.ctrlKey || e.metaKey;
                var letter = e.code && e.code.indexOf("Key") === 0 ? e.code.charCodeAt(3) - 65 : -1;
                // Copy, cut and paste stay with the browser, which only then raises its clipboard events.
                if (chord && !e.altKey && (e.code === "KeyC" || e.code === "KeyX" || e.code === "KeyV")) return;
                var modifiers = (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);
                var code = codes[e.code] || 0;
                if (!code && chord && letter >= 0) code = 18 + letter;
                if (!code) return;
                e.preventDefault();
                state.keyPending = true;
                push({ kind: 4, code: code, modifiers: modifiers, repeat: e.repeat ? 1 : 0 });
            }, true);
            // A key went to the field and has not come back yet, so the box's caret is behind: what is typed now goes by the field's place.
            box.addEventListener("beforeinput", function (e) {
                if (!state.host || !state.keyPending || state.composing || e.isComposing) return;
                if (e.inputType !== "insertText" || !e.data) return;
                e.preventDefault();
                push({ kind: 7, text: e.data });
            });
            box.addEventListener("select", function () {
                if (!state.host || state.composing || state.pushing) return;
                push({ kind: 5, start: box.selectionStart, end: box.selectionEnd });
            });
            box.addEventListener("blur", function () {
                if (!state.host) return;
                // The window going away is no reason to end a session: focus comes back with it.
                setTimeout(function () { if (state.host && document.hasFocus() && document.activeElement !== state.box) push({ kind: 6, text: "done" }); }, 150);
            });
        };
        state.attach = attach;
        attach(state.box);
        window.addEventListener("focus", function () { if (state.host) state.box.focus({ preventScroll: true }); });
        // A touch lands on a published field before Unity has seen it, and only a touch handler may raise a keyboard.
        var isInField = function (point) {
            var canvas = Module.canvas || document.querySelector("canvas");
            if (!canvas) return false;
            var rect = canvas.getBoundingClientRect();
            var x = (point.clientX - rect.left) * canvas.width / rect.width, y = canvas.height - (point.clientY - rect.top) * canvas.height / rect.height;
            for (var i = 0; i + 3 < state.fields.length; i += 4)
                if (x >= state.fields[i] && y >= state.fields[i + 1] && x <= state.fields[i + 2] && y <= state.fields[i + 3]) return true;
            return false;
        };
        var startHeight = function () { return window.visualViewport ? window.visualViewport.height : window.innerHeight; };
        document.addEventListener("touchend", function (e) {
            var touch = e.changedTouches && e.changedTouches[0];
            if (!touch || state.moved || !isInField(touch)) return;
            state.box.focus({ preventScroll: true });
            // Whether a keyboard rose can only be guessed from the page shrinking. Twice with no sign, the next tap lands on a real element instead.
            var before = startHeight();
            setTimeout(function () {
                var shrank = startHeight() < before - 40 && !window.letteraForceNoKeyboardSignal;
                state.silent = shrank ? 0 : state.silent + 1;
                if (state.silent >= 2) state.overlay = true;
            }, 800);
        }, true);
    },

    LetteraHost_Open__deps: ["LetteraHost_Ensure"],
    LetteraHost_Open: function (text, start, end, revision, multiline, secure, kind, returnKey, autocapitalize, autocorrect) {
        _LetteraHost_Ensure();
        var state = Module.letteraInput;
        if (secure) {
            // Browsers have no secure textarea, so a password takes an element of its own, and the rest stays as it was.
            if (!state.password) {
                var input = document.createElement("input");
                input.type = "password";
                input.style.cssText = state.box.style.cssText;
                input.setAttribute("aria-hidden", "true");
                document.body.appendChild(input);
                state.password = input;
                state.textBox = state.box;
                state.attach(input);
            }
            state.box = state.password;
        } else if (state.textBox) state.box = state.textBox;
        var box = state.box;
        state.host = true;
        state.open = true;
        state.composing = false;
        state.multiline = !!multiline;
        state.revision = revision;
        state.queue.length = 0;
        var modes = ["text", "text", "url", "numeric", "decimal", "tel", "text", "email", "search"];
        var keys = ["", "done", "go", "next", "search", "send"];
        var caps = ["off", "sentences", "words", "characters"];
        box.setAttribute("inputmode", modes[kind] || "text");
        if (keys[returnKey]) box.setAttribute("enterkeyhint", keys[returnKey]);
        else box.removeAttribute("enterkeyhint");
        box.setAttribute("autocapitalize", caps[autocapitalize] || "off");
        box.setAttribute("autocorrect", autocorrect ? "on" : "off");
        box.setAttribute("autocomplete", secure ? "off" : "on");
        state.pushing = true;
        box.value = UTF8ToString(text);
        box.setSelectionRange(start, end);
        state.pushing = false;
        box.focus({ preventScroll: true });
    },

    LetteraHost_Push: function (text, start, end, revision) {
        var state = Module.letteraInput;
        if (!state || !state.host || state.composing) return;
        var box = state.box;
        var value = UTF8ToString(text);
        state.revision = revision;
        state.keyPending = false;
        state.pushing = true;
        if (box.value !== value) box.value = value;
        if (box.selectionStart !== start || box.selectionEnd !== end) box.setSelectionRange(start, end);
        setTimeout(function () { state.pushing = false; }, 0);
    },

    LetteraHost_Close: function () {
        var state = Module.letteraInput;
        if (!state) return;
        state.host = false;
        state.open = false;
        state.composing = false;
        state.queue.length = 0;
        state.box.style.pointerEvents = "none";
        state.box.value = "";
        state.box.blur();
    },

    // One event at a time, as ints and a string, so nothing but plain values crosses. The kind is returned: 0 for no more.
    LetteraHost_Next: function (ints, buffer, size) {
        var state = Module.letteraInput;
        if (!state || !state.queue || state.queue.length === 0) return 0;
        var event = state.queue[0];
        var text = event.text || "";
        var needed = lengthBytesUTF8(text) + 1;
        if (needed > size) return -needed;
        state.queue.shift();
        var at = ints >> 2;
        HEAP32[at] = event.revision | 0;
        HEAP32[at + 1] = (event.start === undefined ? -1 : event.start) | 0;
        HEAP32[at + 2] = (event.end === undefined ? -1 : event.end) | 0;
        HEAP32[at + 3] = (event.caret === undefined ? -1 : event.caret) | 0;
        HEAP32[at + 4] = (event.code | 0);
        HEAP32[at + 5] = (event.modifiers | 0);
        HEAP32[at + 6] = (event.repeat | event.commit | 0);
        stringToUTF8(text, buffer, size);
        return event.kind;
    },

    // Where editable fields stand, in canvas pixels from the bottom left, ahead of any touch.
    LetteraHost_SetFields__deps: ["LetteraHost_Ensure"],
    LetteraHost_SetFields: function (rects, count) {
        _LetteraHost_Ensure();
        var fields = [];
        for (var i = 0; i < count; i++) fields.push(HEAPF32[(rects >> 2) + i]);
        Module.letteraInput.fields = fields;
    },

    // A box a real tap lands on, for when script focus raised no keyboard.
    LetteraHost_Place: function (x, y, width, height) {
        var state = Module.letteraInput;
        if (!state) return;
        var canvas = Module.canvas || document.querySelector("canvas");
        var box = state.box;
        if (!canvas) return;
        var rect = canvas.getBoundingClientRect();
        var left = rect.left + x * rect.width / canvas.width, top = rect.top + (canvas.height - y - height) * rect.height / canvas.height;
        box.style.left = left + "px";
        box.style.top = top + "px";
        if (state.overlay && !window.letteraNoOverlay) {
            box.style.width = Math.max(width * rect.width / canvas.width, 40) + "px";
            box.style.height = Math.max(height * rect.height / canvas.height, 20) + "px";
            box.style.pointerEvents = "auto";
            box.style.zIndex = "1000";
        }
    },

    LetteraHost_UsesOverlay: function () {
        var state = Module.letteraInput;
        return state && state.overlay ? 1 : 0;
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
