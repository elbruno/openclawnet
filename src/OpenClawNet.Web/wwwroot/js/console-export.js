// Helper used by AgentConsolePanel.razor to trigger a client-side file download
// for the exported activity log. Avoids server roundtrip; works fully in-browser.
window.consoleExport = window.consoleExport || {
    download: function (filename, content, mimeType) {
        try {
            const blob = new Blob([content], { type: mimeType || 'text/plain;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename || 'agent-activity.txt';
            a.style.display = 'none';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return true;
        } catch (err) {
            console.error('consoleExport.download failed:', err);
            return false;
        }
    }
};
