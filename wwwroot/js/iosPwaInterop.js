window.iosPwa = {
    // 1. Prevent 7-Day Data Eviction
    requestPersistentStorage: async function () {
        if (navigator.storage && navigator.storage.persist) {
            try {
                const persisted = await navigator.storage.persist();
                console.log(`Persistent storage granted: ${persisted}`);
                return persisted;
            } catch (err) {
                console.error("Failed to request persistent storage:", err);
                return false;
            }
        }
        return false;
    },

    // 2. Selfie Verification & Canvas-Based Image Compression
    compressImage: function (fileInput) {
        return new Promise((resolve, reject) => {
            if (!fileInput || !fileInput.files || fileInput.files.length === 0) {
                resolve(null);
                return;
            }

            const file = fileInput.files[0];
            const reader = new FileReader();
            reader.onload = function (e) {
                const img = new Image();
                img.onload = function () {
                    const canvas = document.createElement('canvas');
                    let width = img.width;
                    let height = img.height;
                    const maxSide = 960;

                    if (width > maxSide || height > maxSide) {
                        if (width > height) {
                            height = Math.round((height * maxSide) / width);
                            width = maxSide;
                        } else {
                            width = Math.round((width * maxSide) / height);
                            height = maxSide;
                        }
                    }

                    canvas.width = width;
                    canvas.height = height;

                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, width, height);

                    // Compress to 75% quality JPEG
                    const dataUrl = canvas.toDataURL('image/jpeg', 0.75);
                    // Extract base64 payload
                    const base64 = dataUrl.split(',')[1];
                    resolve(base64);
                };
                img.onerror = function (err) {
                    reject("Failed to load image for compression");
                };
                img.src = e.target.result;
            };
            reader.onerror = function (err) {
                reject("Failed to read image file");
            };
            reader.readAsDataURL(file);
        });
    },

    // 3. Visual Geofencing (Leaflet.js)
    mapInstance: null,
    geofenceCircle: null,
    userMarker: null,
    geofenceCenter: null,
    geofenceRadius: 0,

    initializeMap: function (elementId, lat, lng, radius) {
        if (!window.L) {
            console.error("Leaflet.js is not loaded");
            return;
        }

        // Clean up existing map instance
        if (this.mapInstance) {
            this.mapInstance.remove();
        }

        this.geofenceCenter = [lat, lng];
        this.geofenceRadius = radius;

        this.mapInstance = L.map(elementId).setView([lat, lng], 16);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© OpenStreetMap contributors'
        }).addTo(this.mapInstance);

        // Geofence visual representation
        this.geofenceCircle = L.circle([lat, lng], {
            color: '#ff4d4d',
            fillColor: '#ff4d4d',
            fillOpacity: 0.2,
            radius: radius
        }).addTo(this.mapInstance);

        // Initial user marker at center
        this.userMarker = L.marker([lat, lng]).addTo(this.mapInstance)
            .bindPopup("Establishing position...")
            .openPopup();
    },

    updateUserPosition: function (lat, lng) {
        if (!this.mapInstance || !this.userMarker) return;

        const pos = [lat, lng];
        this.userMarker.setLatLng(pos);
        this.mapInstance.setView(pos);

        // Calculate distance
        const distance = this.mapInstance.distance(pos, this.geofenceCenter);
        const inside = distance <= this.geofenceRadius;

        if (inside) {
            this.geofenceCircle.setStyle({ color: '#2ecc71', fillColor: '#2ecc71' });
            this.userMarker.setPopupContent("Inside Geofence ✔").openPopup();
        } else {
            this.geofenceCircle.setStyle({ color: '#ff4d4d', fillColor: '#ff4d4d' });
            this.userMarker.setPopupContent(`Outside Geofence (${Math.round(distance - this.geofenceRadius)}m away) ❌`).openPopup();
        }

        return inside;
    },

    // 4. Online/Offline Listener for active-window sync triggers
    registerOnlineCallback: function (dotNetHelper) {
        window.addEventListener('online', () => {
            dotNetHelper.invokeMethodAsync('OnConnectionChanged', true);
        });
        window.addEventListener('offline', () => {
            dotNetHelper.invokeMethodAsync('OnConnectionChanged', false);
        });
        return navigator.onLine;
    },

    // 5. PWA to Native Swift Bridge State Syncing
    notifyNativeStatusChanged: function (status, timestampMs) {
        if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.attendanceBridge) {
            window.webkit.messageHandlers.attendanceBridge.postMessage({
                status: status,
                timestamp: timestampMs
            });
            console.log(`Pushed status: ${status} with timestamp: ${timestampMs} to Swift WKWebView`);
        } else {
            console.warn("WKWebView bridge is not available.");
        }
    },

    // 6. Interactive Widget to PWA Trigger Setup
    dotNetWidgetHelper: null,
    registerWidgetCallback: function (dotNetHelper) {
        this.dotNetWidgetHelper = dotNetHelper;
    },
    
    onWidgetToggle: function () {
        if (this.dotNetWidgetHelper) {
            console.log("Widget toggle triggered from Swift! Relaying to Blazor...");
            this.dotNetWidgetHelper.invokeMethodAsync('OnWidgetTogglePressed');
        } else {
            console.warn("Blazor widget callback is not registered.");
        }
    }
};
