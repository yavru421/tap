using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tap.Client.Models;

namespace Tap.Client.Services
{
    public class LocalTimeTrackingService : ILocalTimeTrackingService
    {
        private readonly IJSRuntime _jsRuntime;
        private const string StorageKey = "tap_session_queue";

        public LocalTimeTrackingService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<List<SessionRecord>> GetRecordsAsync()
        {
            var json = await _jsRuntime.InvokeAsync<string>("tapInterop.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
            {
                return new List<SessionRecord>();
            }
            return JsonSerializer.Deserialize<List<SessionRecord>>(json) ?? new List<SessionRecord>();
        }

        public async Task ClockInAsync(string lat, string lng)
        {
            var records = await GetRecordsAsync();
            if (records.Any(r => r.IsActive)) return; // Already clocked in

            bool isFirstRecord = !records.Any();

            records.Add(new SessionRecord
            {
                InTime = DateTime.Now,
                Latitude = lat,
                Longitude = lng,
                Synced = false
            });

            await SaveRecordsAsync(records);

            if (isFirstRecord)
            {
                await ExportToMarkdownAsync();
            }
        }

        public async Task ClockOutAsync()
        {
            var records = await GetRecordsAsync();
            var activeSession = records.FirstOrDefault(r => r.IsActive);
            if (activeSession != null)
            {
                activeSession.OutTime = DateTime.Now;
                activeSession.Synced = false;
                await SaveRecordsAsync(records);
            }
        }

        private async Task SaveRecordsAsync(List<SessionRecord> records)
        {
            var json = JsonSerializer.Serialize(records);
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, json);
        }

        public async Task<bool> ExportToMarkdownAsync()
        {
            var records = await GetRecordsAsync();
            var isFsSupported = await _jsRuntime.InvokeAsync<bool>("tapInterop.isFileSystemSupported");
            var sb = new StringBuilder();

            // Calculate total time
            var totalTicks = records.Where(r => !r.IsActive && r.Duration.HasValue).Sum(r => r.Duration.Value.Ticks);
            var totalTime = TimeSpan.FromTicks(totalTicks);

            if (!isFsSupported)
            {
                // Safari fallback: Always download fresh full file
                sb.AppendLine("# Time and Place (TAP) - Activity Log");
                sb.AppendLine("");
                sb.AppendLine("| Clock In | Clock Out | Location | Duration |");
                sb.AppendLine("|---|---|---|---|");
                
                foreach (var record in records.OrderBy(r => r.InTime))
                {
                    var outStr = record.IsActive ? "ACTIVE" : record.OutTime.Value.ToString("g");
                    var durStr = record.IsActive ? "-" : $"{Math.Floor(record.Duration.Value.TotalHours)}h {record.Duration.Value.Minutes}m";
                    var loc = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : $"{record.Latitude}, {record.Longitude}";
                    
                    sb.AppendLine($"| {record.InTime:g} | {outStr} | {loc} | {durStr} |");
                }
                
                sb.AppendLine("");
                sb.AppendLine($"**Total Time Tracked**: {Math.Floor(totalTime.TotalHours)}h {totalTime.Minutes}m");

                var success = await _jsRuntime.InvokeAsync<bool>("tapInterop.safariDownload", "tap-timesheet.md", sb.ToString());
                if (!success) return false;

                foreach(var record in records) record.Synced = true;
                await SaveRecordsAsync(records);
                return true;
            }

            // Chrome/Edge partial syncing is complex if we re-calculate totals, so we also just overwrite the file entirely for consistency in file parsing!
            // Actually, if we use the file as a database, we should always overwrite the whole file so it has the accurate sum and edited rows.
            sb.AppendLine("# Time and Place (TAP) - Activity Log");
            sb.AppendLine("");
            sb.AppendLine("| Clock In | Clock Out | Location | Duration |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var record in records.OrderBy(r => r.InTime))
            {
                var outStr = record.IsActive ? "ACTIVE" : record.OutTime.Value.ToString("g");
                var durStr = record.IsActive ? "-" : $"{Math.Floor(record.Duration.Value.TotalHours)}h {record.Duration.Value.Minutes}m";
                var loc = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : $"{record.Latitude}, {record.Longitude}";
                sb.AppendLine($"| {record.InTime:g} | {outStr} | {loc} | {durStr} |");
            }
            sb.AppendLine("");
            sb.AppendLine($"**Total Time Tracked**: {Math.Floor(totalTime.TotalHours)}h {totalTime.Minutes}m");

            var fsSuccess = await _jsRuntime.InvokeAsync<bool>("tapInterop.pickAndSaveFile", sb.ToString());
            if (!fsSuccess) return false;

            foreach(var record in records) record.Synced = true;
            await SaveRecordsAsync(records);
            return true;
        }

        public async Task LoadFromMarkdownAsync(string markdownText)
        {
            var lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var records = new List<SessionRecord>();

            foreach (var line in lines)
            {
                if (line.StartsWith("|") && !line.Contains("---|") && !line.Contains("Clock In | Clock Out"))
                {
                    var cols = line.Split('|').Select(c => c.Trim()).ToArray();
                    if (cols.Length >= 4)
                    {
                        var inTimeStr = cols[1];
                        var outTimeStr = cols[2];
                        var locStr = cols[3];

                        if (DateTime.TryParse(inTimeStr, out var inTime))
                        {
                            var record = new SessionRecord { InTime = inTime, Synced = true };
                            
                            if (DateTime.TryParse(outTimeStr, out var outTime))
                            {
                                record.OutTime = outTime;
                            }
                            
                            if (locStr != "Unknown" && locStr.Contains(","))
                            {
                                var parts = locStr.Split(',');
                                if(parts.Length == 2)
                                {
                                    record.Latitude = parts[0].Trim();
                                    record.Longitude = parts[1].Trim();
                                }
                            }
                            
                            records.Add(record);
                        }
                    }
                }
            }

            await SaveRecordsAsync(records);
        }

        public async Task ClearAllRecordsAsync()
        {
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, "[]");
        }
    }
}
