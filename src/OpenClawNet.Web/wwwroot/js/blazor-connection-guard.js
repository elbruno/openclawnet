(function () {
    const dedupeWindowMs = 15000;
    const lastSeenByMessage = new Map();
    const originalConsoleError = console.error.bind(console);

    function normalizeMessage(args) {
        return args
            .map((value) => {
                if (typeof value === "string") {
                    return value;
                }

                if (value instanceof Error) {
                    return value.message;
                }

                try {
                    return JSON.stringify(value);
                } catch {
                    return String(value);
                }
            })
            .join(" ");
    }

    function isBlazorConnectivityFailure(message) {
        return message.includes("Failed to complete negotiation with the server")
            || message.includes("Failed to start the connection")
            || message.includes("_blazor/negotiate")
            || message.includes("ERR_CONNECTION_REFUSED");
    }

    function showServicesUnavailableHint() {
        const reconnectModal = document.getElementById("components-reconnect-modal");
        if (!(reconnectModal instanceof HTMLDialogElement)) {
            return;
        }

        reconnectModal.classList.remove(
            "components-reconnect-show",
            "components-reconnect-retrying",
            "components-reconnect-paused",
            "components-reconnect-resume-failed");
        reconnectModal.classList.add("components-reconnect-failed");

        if (!reconnectModal.open) {
            reconnectModal.showModal();
        }
    }

    console.error = (...args) => {
        const message = normalizeMessage(args);
        if (isBlazorConnectivityFailure(message)) {
            showServicesUnavailableHint();

            const now = Date.now();
            const lastSeen = lastSeenByMessage.get(message) ?? 0;
            if (now - lastSeen < dedupeWindowMs) {
                return;
            }

            lastSeenByMessage.set(message, now);
        }

        originalConsoleError(...args);
    };

    window.addEventListener("unhandledrejection", (event) => {
        const message = normalizeMessage([event.reason]);
        if (isBlazorConnectivityFailure(message)) {
            showServicesUnavailableHint();
        }
    });
})();
