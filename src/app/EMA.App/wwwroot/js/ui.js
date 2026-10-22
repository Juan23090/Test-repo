window.emaUi = Object.freeze({
    scrollIntoView: function (elementId) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        document.getElementById(elementId)?.scrollIntoView({
            behavior: "smooth",
            block: "start"
        });
    },

    makeDraggable: function (elementId, handleSelector) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        const element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        const handle =
            typeof handleSelector === "string"
                ? element.querySelector(handleSelector)
                : element;

        if (!handle || element.__emaDragCleanup) {
            return;
        }

        let pointerId = null;
        let offsetX = 0;
        let offsetY = 0;

        const clamp = function (value, min, max) {
            return Math.min(Math.max(value, min), max);
        };

        const onPointerMove = function (event) {
            if (pointerId === null || event.pointerId !== pointerId) {
                return;
            }

            const width = element.offsetWidth;
            const height = element.offsetHeight;
            const maxLeft = Math.max(0, window.innerWidth - width);
            const maxTop = Math.max(0, window.innerHeight - height);

            const nextLeft = clamp(event.clientX - offsetX, 0, maxLeft);
            const nextTop = clamp(event.clientY - offsetY, 0, maxTop);

            element.style.right = "auto";
            element.style.left = `${nextLeft}px`;
            element.style.top = `${nextTop}px`;
            element.style.transform = "none";
        };

        const stopDragging = function (event) {
            if (pointerId === null) {
                return;
            }

            if (event && event.pointerId !== pointerId) {
                return;
            }

            try {
                handle.releasePointerCapture(pointerId);
            } catch {
                // The pointer may already have been released by the browser.
            }

            pointerId = null;
        };

        const onPointerDown = function (event) {
            if (event.button !== 0) {
                return;
            }

            if (event.target.closest("button, a, input, select, textarea, [data-ema-resize]")) {
                return;
            }

            const rect = element.getBoundingClientRect();

            pointerId = event.pointerId;
            offsetX = event.clientX - rect.left;
            offsetY = event.clientY - rect.top;

            element.style.right = "auto";
            element.style.left = `${rect.left}px`;
            element.style.top = `${rect.top}px`;
            element.style.transform = "none";

            handle.setPointerCapture(pointerId);
            event.preventDefault();
        };

        const onResize = function () {
            const rect = element.getBoundingClientRect();
            const maxLeft = Math.max(0, window.innerWidth - rect.width);
            const maxTop = Math.max(0, window.innerHeight - rect.height);

            element.style.right = "auto";
            element.style.left = `${clamp(rect.left, 0, maxLeft)}px`;
            element.style.top = `${clamp(rect.top, 0, maxTop)}px`;
            element.style.transform = "none";
        };

        handle.addEventListener("pointerdown", onPointerDown);
        handle.addEventListener("pointermove", onPointerMove);
        handle.addEventListener("pointerup", stopDragging);
        handle.addEventListener("pointercancel", stopDragging);
        window.addEventListener("resize", onResize);

        element.__emaDragCleanup = function () {
            handle.removeEventListener("pointerdown", onPointerDown);
            handle.removeEventListener("pointermove", onPointerMove);
            handle.removeEventListener("pointerup", stopDragging);
            handle.removeEventListener("pointercancel", stopDragging);
            window.removeEventListener("resize", onResize);
            delete element.__emaDragCleanup;
        };
    },

    makeResizable: function (elementId, minWidth, minHeight) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        const element = document.getElementById(elementId);
        if (!element || element.__emaResizeCleanup) {
            return;
        }

        const handles = Array.from(
            element.querySelectorAll("[data-ema-resize]"));

        if (handles.length === 0) {
            return;
        }

        const minimumWidth =
            Number.isFinite(Number(minWidth)) ? Number(minWidth) : 260;
        const minimumHeight =
            Number.isFinite(Number(minHeight)) ? Number(minHeight) : 170;

        let activeHandle = null;
        let pointerId = null;
        let direction = "";
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;
        let startWidth = 0;
        let startHeight = 0;

        const clamp = function (value, min, max) {
            return Math.min(Math.max(value, min), max);
        };

        const onPointerMove = function (event) {
            if (
                pointerId === null ||
                event.pointerId !== pointerId ||
                !activeHandle
            ) {
                return;
            }

            const deltaX = event.clientX - startX;
            const deltaY = event.clientY - startY;

            let nextLeft = startLeft;
            let nextTop = startTop;
            let nextWidth = startWidth;
            let nextHeight = startHeight;

            if (direction.includes("e")) {
                nextWidth = clamp(
                    startWidth + deltaX,
                    minimumWidth,
                    window.innerWidth - startLeft);
            }

            if (direction.includes("s")) {
                nextHeight = clamp(
                    startHeight + deltaY,
                    minimumHeight,
                    window.innerHeight - startTop);
            }

            if (direction.includes("w")) {
                const maxWidth = startLeft + startWidth;
                nextWidth = clamp(
                    startWidth - deltaX,
                    minimumWidth,
                    maxWidth);
                nextLeft = startLeft + (startWidth - nextWidth);
            }

            if (direction.includes("n")) {
                const maxHeight = startTop + startHeight;
                nextHeight = clamp(
                    startHeight - deltaY,
                    minimumHeight,
                    maxHeight);
                nextTop = startTop + (startHeight - nextHeight);
            }

            element.style.right = "auto";
            element.style.left = `${nextLeft}px`;
            element.style.top = `${nextTop}px`;
            element.style.width = `${nextWidth}px`;
            element.style.height = `${nextHeight}px`;
            element.style.transform = "none";
        };

        const stopResizing = function (event) {
            if (pointerId === null || !activeHandle) {
                return;
            }

            if (event && event.pointerId !== pointerId) {
                return;
            }

            try {
                activeHandle.releasePointerCapture(pointerId);
            } catch {
                // The pointer may already have been released by the browser.
            }

            pointerId = null;
            activeHandle = null;
            direction = "";
        };

        const onPointerDown = function (event) {
            if (event.button !== 0) {
                return;
            }

            const handle = event.currentTarget;
            const rect = element.getBoundingClientRect();

            activeHandle = handle;
            pointerId = event.pointerId;
            direction = handle.dataset.emaResize || "";
            startX = event.clientX;
            startY = event.clientY;
            startLeft = rect.left;
            startTop = rect.top;
            startWidth = rect.width;
            startHeight = rect.height;

            element.style.right = "auto";
            element.style.left = `${rect.left}px`;
            element.style.top = `${rect.top}px`;
            element.style.width = `${rect.width}px`;
            element.style.height = `${rect.height}px`;
            element.style.transform = "none";

            handle.setPointerCapture(pointerId);
            event.preventDefault();
            event.stopPropagation();
        };

        handles.forEach(function (handle) {
            handle.addEventListener("pointerdown", onPointerDown);
            handle.addEventListener("pointermove", onPointerMove);
            handle.addEventListener("pointerup", stopResizing);
            handle.addEventListener("pointercancel", stopResizing);
        });

        element.__emaResizeCleanup = function () {
            handles.forEach(function (handle) {
                handle.removeEventListener("pointerdown", onPointerDown);
                handle.removeEventListener("pointermove", onPointerMove);
                handle.removeEventListener("pointerup", stopResizing);
                handle.removeEventListener("pointercancel", stopResizing);
            });

            delete element.__emaResizeCleanup;
        };
    },

    releaseDraggable: function (elementId) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        const element = document.getElementById(elementId);
        element?.__emaDragCleanup?.();
    },

    releaseResizable: function (elementId) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        const element = document.getElementById(elementId);
        element?.__emaResizeCleanup?.();
    },

    mediaStop: function (element) {
        if (!element) {
            return;
        }

        try {
            element.pause();
            element.currentTime = 0;
        } catch {
            // Media cleanup is best-effort.
        }
    },

    mediaRewind: function (element, seconds) {
        if (!element) {
            return;
        }

        const amount = Number.isFinite(Number(seconds))
            ? Math.max(0, Number(seconds))
            : 5;

        try {
            element.currentTime = Math.max(0, element.currentTime - amount);
        } catch {
            // Ignore media elements that are not ready yet.
        }
    },

    mediaReplay: function (element) {
        if (!element) {
            return;
        }

        try {
            element.currentTime = 0;
            const playResult = element.play();

            if (playResult && typeof playResult.catch === "function") {
                playResult.catch(function () {
                    // Browser autoplay policies can reject play(); native controls remain available.
                });
            }
        } catch {
            // Ignore media elements that are not ready yet.
        }
    },

    speechStart: function (text) {
        if (!window.speechSynthesis || typeof window.SpeechSynthesisUtterance !== "function") {
            return false;
        }

        const script = typeof text === "string" ? text.trim() : "";
        if (!script) {
            return false;
        }

        const state = window.__emaSpeechState || {
            text: "",
            currentChar: 0,
            utteranceStartChar: 0,
            token: 0
        };

        window.__emaSpeechState = state;
        state.text = script;
        state.currentChar = 0;
        state.utteranceStartChar = 0;
        state.token += 1;

        window.speechSynthesis.cancel();

        const speakFrom = function (startChar) {
            if (!state.text) {
                return false;
            }

            const safeStart = Math.max(0, Math.min(startChar || 0, state.text.length));
            const remaining = state.text.slice(safeStart).trimStart();
            const trimOffset = state.text.slice(safeStart).length - remaining.length;
            const actualStart = safeStart + trimOffset;

            if (!remaining) {
                return false;
            }

            state.currentChar = actualStart;
            state.utteranceStartChar = actualStart;
            state.token += 1;
            const token = state.token;

            const utterance = new SpeechSynthesisUtterance(remaining);
            utterance.onboundary = function (event) {
                if (token !== state.token) {
                    return;
                }

                if (Number.isFinite(event.charIndex)) {
                    state.currentChar = Math.min(
                        state.text.length,
                        state.utteranceStartChar + event.charIndex
                    );
                }
            };
            utterance.onend = function () {
                if (token === state.token) {
                    state.currentChar = state.text.length;
                }
            };
            utterance.onerror = function () {
                // The browser can emit an interruption/cancel error during stop/rewind.
            };

            window.speechSynthesis.speak(utterance);
            return true;
        };

        state.speakFrom = speakFrom;
        return speakFrom(0);
    },

    speechStop: function () {
        const state = window.__emaSpeechState;
        if (state) {
            state.token += 1;
            state.currentChar = 0;
        }

        window.speechSynthesis?.cancel();
    },

    speechCancel: function () {
        const state = window.__emaSpeechState;
        if (state) {
            state.token += 1;
        }

        window.speechSynthesis?.cancel();
    },

    speechReplay: function () {
        const state = window.__emaSpeechState;
        if (!state?.text || typeof state.speakFrom !== "function") {
            return false;
        }

        state.token += 1;
        window.speechSynthesis?.cancel();
        return state.speakFrom(0);
    },

    speechRewind: function (seconds) {
        const state = window.__emaSpeechState;
        if (!state?.text || typeof state.speakFrom !== "function") {
            return false;
        }

        const amount = Number.isFinite(Number(seconds))
            ? Math.max(0, Number(seconds))
            : 5;

        // Web Speech exposes character/word boundary progress but no media timeline.
        // Approximate rewind using normal narration speed (~150 words/minute).
        const wordsToRewind = Math.max(1, Math.ceil(amount * 2.5));
        const wordStarts = [];
        const regex = /\S+/g;
        let match;

        while ((match = regex.exec(state.text)) !== null) {
            wordStarts.push(match.index);
        }

        if (wordStarts.length === 0) {
            return state.speakFrom(0);
        }

        let currentWordIndex = 0;
        for (let i = 0; i < wordStarts.length; i += 1) {
            if (wordStarts[i] <= state.currentChar) {
                currentWordIndex = i;
            } else {
                break;
            }
        }

        const targetIndex = Math.max(0, currentWordIndex - wordsToRewind);
        const targetChar = wordStarts[targetIndex] || 0;

        state.token += 1;
        window.speechSynthesis?.cancel();
        return state.speakFrom(targetChar);
    },

    releaseFloatingPanel: function (elementId) {
        if (typeof elementId !== "string" || elementId.length > 128) {
            return;
        }

        const element = document.getElementById(elementId);
        element?.__emaDragCleanup?.();
        element?.__emaResizeCleanup?.();
    }
});
