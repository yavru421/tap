using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Tap.Client.Models;

namespace Tap.Client.Services
{
    public class SyncService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private DotNetObjectReference<SyncService>? _dotNetHelper;
        private const string StorageKey = "clock_in_entries";

        public bool IsOnline { get; private set; } = true;
        public List<ClockInEntry> CacheQueue { get; private set; } = new();

        public event Action? OnSyncStatusChanged;

        public SyncService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            _dotNetHelper = DotNetObjectReference.Create(this);
            
            // Load from cache
            var stored = await _jsRuntime.InvokeAsync<string?>("tapInterop.getItem", StorageKey);
            if (!string.IsNullOrEmpty(stored))
            {
                try
                {
                    CacheQueue = JsonSerializer.Deserialize<List<ClockInEntry>>(stored) ?? new();
                }
                catch
                {
                    CacheQueue = new();
                }
            }

            // Register JS interop connection status callback
            IsOnline = await _jsRuntime.InvokeAsync<bool>("iosPwa.registerOnlineCallback", _dotNetHelper);
            
            if (IsOnline)
            {
                await SyncQueueAsync();
            }
        }

        [JSInvokable]
        public async Task OnConnectionChanged(bool isOnline)
        {
            IsOnline = isOnline;
            NotifyStateChanged();

            if (isOnline)
            {
                // Active-Window Syncing (No Background Sync)
                // When coming back online, run sync automatically
                await SyncQueueAsync();
            }
        }

        public async Task AddEntryAsync(ClockInEntry entry)
        {
            CacheQueue.Insert(0, entry);
            await SaveToStorageAsync();
            NotifyStateChanged();

            if (IsOnline)
            {
                await SyncQueueAsync();
            }
        }

        public async Task SyncQueueAsync()
        {
            if (!IsOnline) return;

            bool changed = false;
            foreach (var entry in CacheQueue.Where(e => !e.IsSynced && !e.SyncSkip))
            {
                // Process only non-synced and non-skipped entries
                bool success = await SimulateServerUpload(entry);
                if (success)
                {
                    entry.IsSynced = true;
                    entry.SyncFailed = false;
                    entry.ErrorMessage = null;
                    changed = true;
                }
                else
                {
                    entry.SyncFailed = true;
                    entry.ErrorMessage = "Cellular signal too weak (503 Service Unavailable)";
                    changed = true;
                }
            }

            if (changed)
            {
                await SaveToStorageAsync();
                NotifyStateChanged();
            }
        }

        public async Task RetryEntryAsync(string id)
        {
            var entry = CacheQueue.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                entry.SyncFailed = false;
                entry.SyncSkip = false;
                entry.ErrorMessage = null;
                await SaveToStorageAsync();
                NotifyStateChanged();

                if (IsOnline)
                {
                    // Force upload of this specific entry
                    bool success = await SimulateServerUpload(entry, forceSuccess: true);
                    if (success)
                    {
                        entry.IsSynced = true;
                        entry.SyncFailed = false;
                    }
                    else
                    {
                        entry.SyncFailed = true;
                        entry.ErrorMessage = "Upload failed during retry.";
                    }
                    await SaveToStorageAsync();
                    NotifyStateChanged();
                }
            }
        }

        public async Task SkipEntryAsync(string id)
        {
            var entry = CacheQueue.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                entry.SyncSkip = true;
                entry.ErrorMessage = "Skipped by user";
                await SaveToStorageAsync();
                NotifyStateChanged();
            }
        }

        private async Task<bool> SimulateServerUpload(ClockInEntry entry, bool forceSuccess = false)
        {
            // Simulate network latency
            await Task.Delay(1500);

            if (forceSuccess) return true;

            // Random failure simulation to show the "Retry / Skip" UI (20% failure chance)
            var rand = new Random();
            return rand.NextDouble() > 0.2;
        }

        private async Task SaveToStorageAsync()
        {
            var serialized = JsonSerializer.Serialize(CacheQueue);
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, serialized);
        }

        private void NotifyStateChanged()
        {
            OnSyncStatusChanged?.Invoke();
        }

        public async ValueTask DisposeAsync()
        {
            _dotNetHelper?.Dispose();
            await Task.CompletedTask;
        }
    }
}
