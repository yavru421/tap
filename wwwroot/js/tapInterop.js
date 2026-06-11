window.tapInterop = {
    // Keep reference to the file handle
    fileHandle: null,

    // 1. Geolocation
    getCurrentLocation: async () => {
        return new Promise((resolve, reject) => {
            if (!navigator.geolocation) {
                reject("Geolocation not supported");
                return;
            }
            navigator.geolocation.getCurrentPosition(
                pos => resolve({ lat: pos.coords.latitude.toFixed(5), lng: pos.coords.longitude.toFixed(5) }),
                err => reject(err.message),
                { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
            );
        });
    },

    // 2. File System Access API / Safari Fallback
    isFileSystemSupported: () => {
        return !!window.showSaveFilePicker;
    },

    safariDownload: (filename, content) => {
        const blob = new Blob([content], { type: 'text/markdown' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        return true;
    },

    pickAndSaveFile: async (initialContent) => {
        if (!window.showSaveFilePicker) {
            return window.tapInterop.safariDownload('record.md', initialContent);
        }
        try {
            const handle = await window.showSaveFilePicker({
                suggestedName: 'record.md',
                types: [{
                    description: 'Markdown File',
                    accept: { 'text/markdown': ['.md'] },
                }],
            });
            window.tapInterop.fileHandle = handle;
            
            const writable = await handle.createWritable();
            await writable.write(initialContent);
            await writable.close();
            return true;
        } catch (err) {
            console.error("File picker cancelled or failed:", err);
            return false;
        }
    },

    appendToFile: async (contentToAppend) => {
        if (!window.showSaveFilePicker || !window.tapInterop.fileHandle) {
            return false; // Should not happen if C# checks isFileSystemSupported
        }
        try {
            const file = await window.tapInterop.fileHandle.getFile();
            const currentSize = file.size;

            const writable = await window.tapInterop.fileHandle.createWritable({ keepExistingData: true });
            await writable.write({ type: 'write', position: currentSize, data: contentToAppend });
            await writable.close();
            return true;
        } catch (err) {
            console.error("Failed to append to file:", err);
            return false;
        }
    },

    hasFileHandle: () => {
        return window.tapInterop.fileHandle !== null;
    },

    // 3. Basic Local Storage
    setItem: (key, value) => {
        localStorage.setItem(key, value);
    },
    getItem: (key) => {
        return localStorage.getItem(key);
    }
};
