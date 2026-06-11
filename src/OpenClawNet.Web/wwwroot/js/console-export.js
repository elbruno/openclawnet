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
    },
    downloadBinary: function (filename, base64Content, mimeType) {
        try {
            const binary = atob(base64Content || '');
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            const blob = new Blob([bytes], { type: mimeType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename || 'export.bin';
            a.style.display = 'none';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return true;
        } catch (err) {
            console.error('consoleExport.downloadBinary failed:', err);
            return false;
        }
    }
};
