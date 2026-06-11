using Microsoft.JSInterop;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tap.Client.Models;

namespace Tap.Client.Services
{
    public class LocalTimeTrackingService : ILocalTimeTrackingService
    {
        private readonly IJSRuntime _jsRuntime;
        private const string StorageKey = "tap_activity_queue";

        public LocalTimeTrackingService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<List<ActivityRecord>> GetRecordsAsync()
        {
            var json = await _jsRuntime.InvokeAsync<string>("tapInterop.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
            {
                return new List<ActivityRecord>();
            }
            return JsonSerializer.Deserialize<List<ActivityRecord>>(json) ?? new List<ActivityRecord>();
        }

        public async Task AddRecordAsync(ActivityRecord record)
        {
            var records = await GetRecordsAsync();
            record.Synced = false;
            records.Add(record);
            var json = JsonSerializer.Serialize(records);
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, json);
        }

        public async Task<bool> ExportToMarkdownAsync()
        {
            var records = await GetRecordsAsync();
            var isFsSupported = await _jsRuntime.InvokeAsync<bool>("tapInterop.isFileSystemSupported");
            
            var sb = new StringBuilder();

            if (!isFsSupported)
            {
                // Safari fallback: Always download a fresh, complete file with all records
                sb.AppendLine("# Time and Place (TAP) - Activity Log");
                sb.AppendLine("");
                sb.AppendLine("| Type | Timestamp | Latitude | Longitude |");
                sb.AppendLine("|---|---|---|---|");
                
                foreach (var record in records.OrderBy(r => r.Timestamp))
                {
                    var lat = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : record.Latitude;
                    var lng = string.IsNullOrEmpty(record.Longitude) ? "Unknown" : record.Longitude;
                    sb.AppendLine($"| {record.Type} | {record.Timestamp:g} | {lat} | {lng} |");
                }

                var success = await _jsRuntime.InvokeAsync<bool>("tapInterop.safariDownload", "tap-timesheet.md", sb.ToString());
                if (!success) return false;

                // Mark all as synced
                foreach(var record in records) record.Synced = true;
                var jsonFallback = JsonSerializer.Serialize(records);
                await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, jsonFallback);
                return true;
            }

            // Chrome/Edge flow (File System Access API)
            var unsyncedRecords = records.Where(r => !r.Synced).OrderBy(r => r.Timestamp).ToList();
            if (!unsyncedRecords.Any()) return true; // Already synced

            var hasHandle = await _jsRuntime.InvokeAsync<bool>("tapInterop.hasFileHandle");

            if (!hasHandle)
            {
                sb.AppendLine("# Time and Place (TAP) - Activity Log");
                sb.AppendLine("");
                sb.AppendLine("| Type | Timestamp | Latitude | Longitude |");
                sb.AppendLine("|---|---|---|---|");
                
                foreach (var record in unsyncedRecords)
                {
                    var lat = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : record.Latitude;
                    var lng = string.IsNullOrEmpty(record.Longitude) ? "Unknown" : record.Longitude;
                    sb.AppendLine($"| {record.Type} | {record.Timestamp:g} | {lat} | {lng} |");
                }

                var success = await _jsRuntime.InvokeAsync<bool>("tapInterop.pickAndSaveFile", sb.ToString());
                if (!success) return false;
            }
            else
            {
                foreach (var record in unsyncedRecords)
                {
                    var lat = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : record.Latitude;
                    var lng = string.IsNullOrEmpty(record.Longitude) ? "Unknown" : record.Longitude;
                    sb.AppendLine($"| {record.Type} | {record.Timestamp:g} | {lat} | {lng} |");
                }
                var success = await _jsRuntime.InvokeAsync<bool>("tapInterop.appendToFile", sb.ToString());
                if (!success) return false;
            }

            foreach(var record in unsyncedRecords)
            {
                var match = records.First(r => r.Id == record.Id);
                match.Synced = true;
            }

            var json = JsonSerializer.Serialize(records);
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, json);
            return true;
        }

        public async Task ClearAllRecordsAsync()
        {
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, "[]");
        }
    }
}
