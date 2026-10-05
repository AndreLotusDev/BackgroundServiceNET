// Live status on /Exports: the server pushes "exportUpdated" (ExportsHub) when the Worker's outcome of an export changes.
(() => {
    const table = document.getElementById("export-list");
    if (!table || !window.signalR) {
        return;
    }

    // Same format as the server-rendered cells: "yyyy-MM-dd HH:mm:ss", UTC.
    const formatUtc = value => value ? new Date(value).toISOString().replace("T", " ").slice(0, 19) : "";

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(table.dataset.hubUrl)
        .withAutomaticReconnect()
        .build();

    connection.on("exportUpdated", update => {
        const row = table.querySelector(`tr[data-export-id="${update.exportId}"]`);
        if (!row) {
            return; // Uploaded after this page was loaded (e.g. from another tab).
        }

        const field = name => row.querySelector(`[data-field="${name}"]`);
        const badge = field("status");
        badge.textContent = update.statusText;
        badge.className = update.badgeClass;

        const error = field("error");
        error.textContent = update.error ?? "";
        error.hidden = !update.error;

        field("processed").textContent = update.processedCount;
        field("failed").textContent = update.failedCount;
        field("completed").textContent = formatUtc(update.completedAt);
    });

    // Updates sent while the connection was down are lost; reload to show what web.db has now.
    connection.onreconnected(() => location.reload());

    connection.start().catch(err => console.warn("Live export status is unavailable; reload the page to see changes.", err));
})();
