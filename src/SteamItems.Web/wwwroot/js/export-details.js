// Export details page while the export is Pending or Processing: reload once the Worker's outcome moves on,
// so the rows (or the reason the file was rejected) show without the user refreshing.
(() => {
    const header = document.getElementById("export-header");
    if (!header || !window.signalR) {
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(header.dataset.hubUrl)
        .withAutomaticReconnect()
        .build();

    // Counts change on every poll while processing; only a new status changes what this page shows.
    connection.on("exportUpdated", update => {
        if (update.exportId === header.dataset.exportId && update.status !== header.dataset.status) {
            location.reload();
        }
    });

    // Updates sent while the connection was down are lost; reload to show what web.db has now.
    connection.onreconnected(() => location.reload());

    connection.start().catch(err => console.warn("Live export status is unavailable; reload the page to see changes.", err));
})();
