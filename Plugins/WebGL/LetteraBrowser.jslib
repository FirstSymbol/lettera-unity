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

    // Focus going to the text box makes Unity forget a button still held, with no release. The page itself still knows.
    LetteraInput_TrackMouse: function () {
        if (Module.letteraMouseTracked) return;
        Module.letteraMouseTracked = true;
        Module.letteraMouseHeld = false;
        document.addEventListener("mousedown", function (e) { if (e.button === 0) Module.letteraMouseHeld = true; }, true);
        document.addEventListener("mouseup", function (e) { if (e.button === 0) Module.letteraMouseHeld = false; }, true);
    },

    LetteraInput_MouseHeld: function () {
        return Module.letteraMouseHeld ? 1 : 0;
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
                // A visible control keeps its own caret keys. What changes the text still belongs to the field.
                if (state.replica) {
                    if (e.code === "Enter" || e.code === "NumpadEnter") { e.preventDefault(); push({ kind: 6, text: "return" }); }
                    else if (chord && e.code === "KeyZ") { e.preventDefault(); push({ kind: 6, text: e.shiftKey ? "redo" : "undo" }); }
                    else if (chord && e.code === "KeyY") { e.preventDefault(); push({ kind: 6, text: "redo" }); }
                    else if (e.code === "Escape") { e.preventDefault(); push({ kind: 6, text: "cancel" }); }
                    return;
                }
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
            // The clipboard's events come only to the focused box, and only they may fill it with no prompt. C# answers at once, inside this handler.
            var copied = function (e, cut) {
                if (!state.host) return;
                // Unity listens further up and would write its own, empty, clipboard over this.
                e.stopPropagation();
                e.preventDefault();
                if (box.type === "password") return;
                var clip = Module.letteraClip;
                if (!clip || !clip.callback) return;
                clip.answer = null;
                var callback = clip.callback;
                {{{ makeDynCall('vi', 'callback') }}}(cut ? 1 : 0);
                var answer = clip.answer;
                if (!answer) return;
                if (e.clipboardData) {
                    e.clipboardData.setData("text/plain", answer.plain);
                    if (answer.html) e.clipboardData.setData("text/html", answer.html);
                }
                if (cut) push({ kind: 6, text: "cutCopied" });
            };
            box.addEventListener("copy", function (e) { copied(e, false); });
            box.addEventListener("cut", function (e) { copied(e, true); });
            box.addEventListener("paste", function (e) {
                if (!state.host || !e.clipboardData) return;
                e.stopPropagation();
                e.preventDefault();
                var data = e.clipboardData, encoder = new TextEncoder(), items = [];
                var html = data.getData("text/html"), plain = data.getData("text/plain");
                if (html) items.push({ format: "text/html", bytes: encoder.encode(html) });
                if (plain) items.push({ format: "text/plain", bytes: encoder.encode(plain) });
                var files = [];
                var limit = Module.letteraClip ? Module.letteraClip.maxImage : 0;
                for (var f = 0; data.files && f < data.files.length; f++) {
                    var file = data.files[f];
                    if (file.type.indexOf("image/") === 0 && file.size <= limit) files.push(file);
                }
                var send = function () { if (state.host && items.length) { var event = { kind: 8, items: items }; push(event); } };
                if (!files.length) { send(); return; }
                var left = files.length;
                files.forEach(function (one) {
                    one.arrayBuffer().then(function (buffer) { items.push({ format: one.type, bytes: new Uint8Array(buffer) }); }).catch(function () { }).then(function () { if (--left === 0) send(); });
                });
            });
            box.addEventListener("select", function () {
                if (!state.host || state.composing || state.pushing) return;
                push({ kind: 5, start: box.selectionStart, end: box.selectionEnd });
            });
            box.addEventListener("blur", function () {
                if (!state.host) return;
                // The window going away is no reason to end a session: focus comes back with it. A press on the canvas is the field's own to judge, and the box keeps the keys meanwhile.
                // At once for the canvas, or the first key after a click goes nowhere.
                setTimeout(function () {
                    if (!state.host || !document.hasFocus() || document.activeElement === state.box) return;
                    if (state.pressedCanvas) state.box.focus({ preventScroll: true });
                    else push({ kind: 6, text: "done" });
                }, state.pressedCanvas ? 0 : 150);
            });
        };
        state.attach = attach;
        attach(state.box);
        // Pages add surfaces by name. The plain one cannot be replaced, nor one a session is using.
        var surfaces = {};
        window.LetteraSurfaces = new Proxy(surfaces, {
            set: function (target, name, value) {
                if (name === "default") throw new Error("The default surface cannot be replaced.");
                if (state.replica && state.replica.surface === name) throw new Error("Surface " + String(name) + " is in use by an open session.");
                target[name] = value;
                return true;
            },
            deleteProperty: function (target, name) {
                if (name === "default") throw new Error("The default surface cannot be removed.");
                if (state.replica && state.replica.surface === name) throw new Error("Surface " + String(name) + " is in use by an open session.");
                delete target[name];
                return true;
            },
        });
        state.push = push;
        document.addEventListener("pointerdown", function (e) { state.pressedCanvas = e.target === (Module.canvas || document.querySelector("canvas")); }, true);
        // A press on the canvas would take the focus from the box, and the canvas losing it again makes Unity forget a button still held, which is every right click. So it never moves.
        document.addEventListener("mousedown", function (e) {
            if (state.host && e.target === (Module.canvas || document.querySelector("canvas"))) e.preventDefault();
        }, true);
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
        // Over the field just tapped, so that the next tap lands on a real text box and the browser's own rule raises the keyboard.
        state.showOverlay = function () {
            var canvas = Module.canvas || document.querySelector("canvas");
            if (!canvas || !state.lastTouch) return;
            var rect = canvas.getBoundingClientRect(), px = (state.lastTouch.x - rect.left) * canvas.width / rect.width, py = canvas.height - (state.lastTouch.y - rect.top) * canvas.height / rect.height;
            for (var i = 0; i + 3 < state.fields.length; i += 4) {
                var f = state.fields;
                if (px < f[i] || py < f[i + 1] || px > f[i + 2] || py > f[i + 3]) continue;
                var box = state.box;
                box.style.left = rect.left + f[i] * rect.width / canvas.width + "px";
                box.style.top = rect.top + (canvas.height - f[i + 3]) * rect.height / canvas.height + "px";
                box.style.width = (f[i + 2] - f[i]) * rect.width / canvas.width + "px";
                box.style.height = (f[i + 3] - f[i + 1]) * rect.height / canvas.height + "px";
                box.style.pointerEvents = "auto";
                box.style.zIndex = "1000";
                state.overlayShown = true;
                return;
            }
        };
        var startHeight = function () { return window.visualViewport ? window.visualViewport.height : window.innerHeight; };
        document.addEventListener("touchend", function (e) {
            var touch = e.changedTouches && e.changedTouches[0];
            if (!touch || state.moved || !isInField(touch)) return;
            state.lastTouch = { x: touch.clientX, y: touch.clientY };
            // A real tap on the overlay has to be told where it landed, since the field never saw it.
            if (state.overlayShown) {
                var canvas = Module.canvas || document.querySelector("canvas");
                var area = canvas.getBoundingClientRect();
                push({ kind: 9, start: Math.round((touch.clientX - area.left) * canvas.width / area.width), end: Math.round(canvas.height - (touch.clientY - area.top) * canvas.height / area.height) });
            }
            state.box.focus({ preventScroll: true });
            // Whether a keyboard rose can only be guessed from the page shrinking. Twice with no sign, the next tap lands on a real element instead.
            var before = startHeight();
            setTimeout(function () {
                var shrank = window.letteraForceKeyboardSignal || (startHeight() < before - 40 && !window.letteraForceNoKeyboardSignal);
                state.silent = shrank ? 0 : state.silent + 1;
                if (state.silent >= 2) { state.overlay = true; if (state.host) state.showOverlay(); }
            }, 800);
        }, true);
    },

    LetteraHost_Open__deps: ["LetteraHost_Ensure"],
    LetteraHost_Open: function (text, start, end, revision, multiline, secure, kind, returnKey, autocapitalize, autocorrect, spellCheck, autofill) {
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
        // A hidden box's own underlines and suggestions would not show, so by default it asks for none of them.
        var caps = ["off", "off", "sentences", "words", "characters"];
        box.setAttribute("inputmode", modes[kind] || "text");
        if (keys[returnKey]) box.setAttribute("enterkeyhint", keys[returnKey]);
        else box.removeAttribute("enterkeyhint");
        box.setAttribute("autocapitalize", caps[autocapitalize] || "off");
        box.setAttribute("autocorrect", autocorrect ? "on" : "off");
        var hint = UTF8ToString(autofill);
        box.setAttribute("autocomplete", hint || (secure ? "off" : "on"));
        box.spellcheck = spellCheck === 1;
        state.pushing = true;
        box.value = UTF8ToString(text);
        box.setSelectionRange(start, end);
        state.pushing = false;
        box.focus({ preventScroll: true });
    },

    // The platform's own control in the field's place, through the same events: every rule of the field still decides.
    LetteraHost_OpenReplica__deps: ["LetteraHost_Ensure"],
    LetteraHost_OpenReplica: function (text, start, end, revision, multilineControl, secure, kind, returnKey, autocapitalize, autocorrect, spellCheck, autofill, placeholder, surface, controlId, surfaceData, readOnly) {
        _LetteraHost_Ensure();
        var state = Module.letteraInput;
        var surfaceName = UTF8ToString(surface) || "default";
        var hooks = null;
        if (surfaceName !== "default") {
            hooks = window.LetteraSurfaces[surfaceName];
            if (!hooks) return 0;
        }
        var control;
        if (multilineControl) control = document.createElement("textarea");
        else {
            control = document.createElement("input");
            control.type = secure ? "password" : "text";
        }
        var id = UTF8ToString(controlId);
        if (id) control.id = id;
        control.placeholder = UTF8ToString(placeholder);
        control.readOnly = !!readOnly;
        var modes = ["text", "text", "url", "numeric", "decimal", "tel", "text", "email", "search"];
        var keys = ["", "done", "go", "next", "search", "send"];
        var caps = ["", "off", "sentences", "words", "characters"];
        control.setAttribute("inputmode", modes[kind] || "text");
        if (keys[returnKey]) control.setAttribute("enterkeyhint", keys[returnKey]);
        if (caps[autocapitalize]) control.setAttribute("autocapitalize", caps[autocapitalize]);
        control.setAttribute("autocorrect", autocorrect ? "on" : "off");
        var hint = UTF8ToString(autofill);
        if (hint) control.setAttribute("autocomplete", hint);
        // Here it is seen, so the browser decides unless the field said otherwise.
        if (spellCheck === 1) control.spellcheck = true;
        if (spellCheck === 2) control.spellcheck = false;
        control.style.cssText = "position:fixed;box-sizing:border-box;margin:0;font-size:16px;z-index:1000;resize:none;";

        var open = { closed: false, control: control, surface: surfaceName, hooks: hooks, root: control, previous: state.box, context: null };
        var context = {
            control: control,
            surface: surfaceName,
            data: UTF8ToString(surfaceData),
            rect: { x: 0, y: 0, width: 0, height: 0 },
            performVerb: function (verb) {
                if (open.closed) throw new Error("This session has closed.");
                state.push({ kind: 6, text: String(verb) });
            },
        };
        open.context = context;
        if (hooks) {
            var root = null;
            try { root = hooks.build(context); } catch (error) { console.error(error); }
            if (!root || !root.contains(control)) {
                try { if (hooks.dispose) hooks.dispose(context); } catch (error) { console.error(error); }
                return 0;
            }
            open.root = root;
        }
        document.body.appendChild(open.root);
        if (!control.__letteraAttached) { state.attach(control); control.__letteraAttached = true; }
        var report = function () { if (state.host && state.box === control && !state.pushing) state.push({ kind: 5, start: control.selectionStart, end: control.selectionEnd }); };
        control.addEventListener("keyup", report);
        control.addEventListener("mouseup", report);

        state.replica = open;
        state.box = control;
        state.host = true;
        state.open = true;
        state.composing = false;
        state.multiline = !!multilineControl;
        state.revision = revision;
        state.queue.length = 0;
        state.pushing = true;
        control.value = UTF8ToString(text);
        try { control.setSelectionRange(start, end); } catch (error) { }
        state.pushing = false;
        control.focus({ preventScroll: true });
        return 1;
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
        if (state.replica) {
            var open = state.replica;
            state.replica = null;
            open.closed = true;
            state.box = open.previous;
            if (open.hooks && open.hooks.dispose) { try { open.hooks.dispose(open.context); } catch (error) { console.error(error); } }
            if (open.root.parentNode) open.root.parentNode.removeChild(open.root);
            return;
        }
        state.box.style.pointerEvents = "none";
        state.box.style.zIndex = "-1";
        state.box.style.width = "2px";
        state.box.style.height = "20px";
        state.overlayShown = false;
        state.box.value = "";
        state.box.blur();
    },

    // One event at a time, as ints and a string, so nothing but plain values crosses. The kind is returned: 0 for no more.
    LetteraHost_Next: function (ints, buffer, size) {
        var state = Module.letteraInput;
        if (!state || !state.queue || state.queue.length === 0) return 0;
        var event = state.queue[0];
        if (event.items && Module.letteraClip) Module.letteraClip.current = event.items;
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
        if (canvas && state.replica) {
            // CSS pixels, whatever the canvas's scale and the page's zoom.
            var area = canvas.getBoundingClientRect();
            var scaled = { x: area.left + x * area.width / canvas.width, y: area.top + (canvas.height - y - height) * area.height / canvas.height, width: width * area.width / canvas.width, height: height * area.height / canvas.height };
            var open = state.replica;
            open.context.rect = scaled;
            if (open.hooks) {
                if (open.hooks.arrange) { try { open.hooks.arrange(open.context); } catch (error) { console.error(error); } }
                return;
            }
            var style = open.control.style;
            style.left = scaled.x + "px";
            style.top = scaled.y + "px";
            style.width = scaled.width + "px";
            style.height = scaled.height + "px";
            return;
        }
        var box = state.box;
        if (!canvas || state.overlayShown) return;
        var rect = canvas.getBoundingClientRect();
        var left = rect.left + x * rect.width / canvas.width, top = rect.top + (canvas.height - y - height) * rect.height / canvas.height;
        box.style.left = left + "px";
        box.style.top = top + "px";
    },

    // The clipboard, as far as a page may reach it.
    LetteraClip_Ensure: function () {
        if (Module.letteraClip) return Module.letteraClip;
        var clip = Module.letteraClip = { callback: 0, maxImage: 0, nextId: 0, results: [], current: [], gestures: {}, writesFail: false, gestureRead: null, gestureAt: 0, wroteAt: 0, answer: null };
        clip.write = function (plain, html) {
            var done = function () { clip.writesFail = false; }, fail = function () { clip.writesFail = true; };
            var api = navigator.clipboard;
            if (!api) { fail(); return false; }
            if (window.ClipboardItem && api.write) {
                var parts = { "text/plain": new Blob([plain], { type: "text/plain" }) };
                if (html) parts["text/html"] = new Blob([html], { type: "text/html" });
                api.write([new ClipboardItem(parts)]).then(done, fail);
                return true;
            }
            if (api.writeText) { api.writeText(plain).then(done, fail); return true; }
            fail();
            return false;
        };
        clip.read = function () {
            var api = navigator.clipboard;
            if (!api) return Promise.reject(new Error("none"));
            var encoder = new TextEncoder();
            var plainOnly = function () {
                if (!api.readText) return Promise.reject(new Error("none"));
                return api.readText().then(function (text) { return text ? [{ format: "text/plain", bytes: encoder.encode(text) }] : []; });
            };
            if (!api.read) return plainOnly();
            return api.read().then(function (list) {
                var items = [], work = [];
                list.forEach(function (entry) {
                    entry.types.forEach(function (type) {
                        if (type !== "text/plain" && type !== "text/html" && type.indexOf("image/") !== 0) return;
                        work.push(entry.getType(type).then(function (blob) { return blob.arrayBuffer(); }).then(function (buffer) {
                            if (type.indexOf("image/") !== 0 || buffer.byteLength <= clip.maxImage) items.push({ format: type, bytes: new Uint8Array(buffer) });
                        }));
                    });
                });
                return Promise.all(work).then(function () { return items; });
            }, function () { return plainOnly(); });
        };
        var release = function (e) {
            var now = Date.now();
            if (now - (clip.releaseAt || 0) < 300) return;
            var canvas = Module.canvas || document.querySelector("canvas");
            if (!canvas) return;
            var point = e.changedTouches && e.changedTouches.length ? e.changedTouches[0] : e;
            var box = canvas.getBoundingClientRect();
            var x = (point.clientX - box.left) * canvas.width / box.width, y = canvas.height - (point.clientY - box.top) * canvas.height / box.height;
            for (var verb in clip.gestures) {
                var gesture = clip.gestures[verb], inside = false;
                for (var i = 0; i + 3 < gesture.rects.length; i += 4)
                    inside = inside || (x >= gesture.rects[i] && y >= gesture.rects[i + 1] && x <= gesture.rects[i + 2] && y <= gesture.rects[i + 3]);
                if (!inside) continue;
                clip.releaseAt = now;
                if (verb == 3) {
                    clip.gestureRead = clip.read();
                    clip.gestureRead.catch(function () { });
                    clip.gestureAt = now;
                } else if (gesture.plain) {
                    if (!clip.write(gesture.plain, gesture.html)) { try { document.execCommand("copy"); } catch (error) { } }
                    clip.wroteAt = now;
                }
                return;
            }
        };
        document.addEventListener("touchend", release, true);
        document.addEventListener("mouseup", release, true);
        var ownCopy = function (e) {
            var tag = e.target && e.target.tagName;
            if (tag === "INPUT" || tag === "TEXTAREA" || (e.target && e.target.isContentEditable)) e.stopPropagation();
        };
        document.body.addEventListener("copy", ownCopy);
        document.body.addEventListener("cut", ownCopy);
        return clip;
    },

    LetteraClip_SetCallback__deps: ["LetteraClip_Ensure"],
    LetteraClip_SetCallback: function (callback, maxImage) {
        var clip = _LetteraClip_Ensure();
        clip.callback = callback;
        clip.maxImage = maxImage;
    },

    LetteraClip_Answer__deps: ["LetteraClip_Ensure"],
    LetteraClip_Answer: function (plain, html) {
        _LetteraClip_Ensure().answer = { plain: UTF8ToString(plain), html: UTF8ToString(html) };
    },

    // Unasked by a gesture: a browser may refuse, and then a copy stays within the page. A gesture that just wrote the same thing has already done it.
    LetteraClip_WriteNow__deps: ["LetteraClip_Ensure"],
    LetteraClip_WriteNow: function (plain, html) {
        var clip = _LetteraClip_Ensure();
        if (Date.now() - clip.wroteAt < 2000) return;
        clip.write(UTF8ToString(plain), UTF8ToString(html));
    },

    LetteraClip_Read__deps: ["LetteraClip_Ensure"],
    LetteraClip_Read: function () {
        var clip = _LetteraClip_Ensure();
        var id = ++clip.nextId;
        var read = clip.gestureRead && Date.now() - clip.gestureAt < 3000 ? clip.gestureRead : clip.read();
        clip.gestureRead = null;
        read.then(function (items) { clip.results.push({ id: id, items: items }); }, function () { clip.results.push({ id: id, items: null }); });
        return id;
    },

    // The id of a read that finished, negative when it was refused. Its items are then at hand.
    LetteraClip_NextResult: function () {
        var clip = Module.letteraClip;
        if (!clip || !clip.results.length) return 0;
        var result = clip.results.shift();
        clip.current = result.items || [];
        return result.items ? result.id : -result.id;
    },

    LetteraClip_ItemCount: function () {
        return Module.letteraClip ? Module.letteraClip.current.length : 0;
    },

    LetteraClip_ItemFormat: function (index, buffer, size) {
        var name = Module.letteraClip.current[index].format;
        stringToUTF8(name, buffer, size);
        return lengthBytesUTF8(name);
    },

    LetteraClip_ItemSize: function (index) {
        return Module.letteraClip.current[index].bytes.length;
    },

    LetteraClip_ItemBytes: function (index, target) {
        HEAPU8.set(Module.letteraClip.current[index].bytes, target);
    },

    LetteraClip_WritesFail: function () {
        var clip = Module.letteraClip;
        return clip && (clip.writesFail || !navigator.clipboard) ? 1 : 0;
    },

    LetteraGesture_Arm__deps: ["LetteraClip_Ensure"],
    LetteraGesture_Arm: function (verb, plain, html, rects, count) {
        var list = [];
        for (var i = 0; i < count; i++) list.push(HEAPF32[(rects >> 2) + i]);
        _LetteraClip_Ensure().gestures[verb] = { plain: UTF8ToString(plain), html: UTF8ToString(html), rects: list };
    },

    LetteraGesture_Disarm: function (verb) {
        if (Module.letteraClip) delete Module.letteraClip.gestures[verb];
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

    // Many clusters on one canvas, read back once: setting a canvas's size and reading it are what cost here.
    LetteraEmoji_RenderBatch: function (text, count, positions, pixels, canvasWidth, canvasHeight, output) {
        var state = Module.letteraEmoji;
        var canvas = state.canvas;
        canvas.width = canvasWidth;
        canvas.height = canvasHeight;
        var context = state.context;
        context.font = pixels + 'px "Apple Color Emoji","Segoe UI Emoji","Noto Color Emoji",sans-serif';
        context.textBaseline = "alphabetic";
        context.fillStyle = "#fff";
        var clusters = UTF8ToString(text).split("\u0001");
        var at = positions >> 2;
        for (var i = 0; i < count; i++) context.fillText(clusters[i], HEAP32[at + i * 2], HEAP32[at + i * 2 + 1]);
        HEAPU8.set(context.getImageData(0, 0, canvasWidth, canvasHeight).data, output);
        // A browser limits what all canvases may hold together.
        canvas.width = 1;
        canvas.height = 1;
    },

    LetteraPage_Platform: function () {
        var nav = navigator;
        var name = (nav.userAgentData && nav.userAgentData.platform) || nav.platform || "";
        var bytes = lengthBytesUTF8(name) + 1;
        var buffer = _malloc(bytes);
        stringToUTF8(name, buffer, bytes);
        return buffer;
    },

    LetteraPage_TouchPoints: function () {
        return navigator.maxTouchPoints | 0;
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
