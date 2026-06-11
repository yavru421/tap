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
        private const string SchemaKey = "tap_schema_columns";

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

        public async Task<List<string>> GetSchemaColumnsAsync()
        {
            var json = await _jsRuntime.InvokeAsync<string>("tapInterop.getItem", SchemaKey);
            return string.IsNullOrEmpty(json) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }

        private async Task SaveSchemaColumnsAsync(List<string> columns)
        {
            var json = JsonSerializer.Serialize(columns);
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", SchemaKey, json);
        }

        public async Task ClockInAsync(string lat, string lng, Dictionary<string, string> customFields)
        {
            var records = await GetRecordsAsync();
            if (records.Any(r => r.IsActive)) return; // Already clocked in

            bool isFirstRecord = !records.Any();

            records.Add(new SessionRecord
            {
                InTime = DateTime.Now,
                Latitude = lat,
                Longitude = lng,
                Synced = false,
                CustomFields = customFields ?? new Dictionary<string, string>()
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
            var schemaCols = await GetSchemaColumnsAsync();
            var isFsSupported = await _jsRuntime.InvokeAsync<bool>("tapInterop.isFileSystemSupported");
            var sb = new StringBuilder();

            // Calculate total time
            var totalTicks = records.Where(r => !r.IsActive && r.Duration.HasValue).Sum(r => r.Duration.Value.Ticks);
            var totalTime = TimeSpan.FromTicks(totalTicks);

            sb.AppendLine("# Time and Place (TAP) - Activity Log");
            sb.AppendLine("");

            // Build dynamic headers
            var headerRow = new StringBuilder("| Clock In | Clock Out | Location |");
            foreach (var col in schemaCols) headerRow.Append($" {col} |");
            headerRow.Append(" Duration |");
            sb.AppendLine(headerRow.ToString());

            var separatorRow = new StringBuilder("|---|---|---|");
            foreach (var col in schemaCols) separatorRow.Append("---|");
            separatorRow.Append("---|");
            sb.AppendLine(separatorRow.ToString());

            foreach (var record in records.OrderBy(r => r.InTime))
            {
                var outStr = record.IsActive ? "ACTIVE" : record.OutTime.Value.ToString("g");
                var durStr = record.IsActive ? "-" : $"{Math.Floor(record.Duration.Value.TotalHours)}h {record.Duration.Value.Minutes}m";
                var loc = string.IsNullOrEmpty(record.Latitude) ? "Unknown" : $"{record.Latitude}, {record.Longitude}";
                
                var row = new StringBuilder($"| {record.InTime:g} | {outStr} | {loc} |");
                foreach (var col in schemaCols)
                {
                    row.Append($" {(record.CustomFields.ContainsKey(col) ? record.CustomFields[col] : "")} |");
                }
                row.Append($" {durStr} |");
                sb.AppendLine(row.ToString());
            }

            sb.AppendLine("");
            sb.AppendLine($"**Total Time Tracked**: {Math.Floor(totalTime.TotalHours)}h {totalTime.Minutes}m");

            bool success;
            if (!isFsSupported)
            {
                success = await _jsRuntime.InvokeAsync<bool>("tapInterop.safariDownload", "record.md", sb.ToString());
            }
            else
            {
                success = await _jsRuntime.InvokeAsync<bool>("tapInterop.pickAndSaveFile", sb.ToString());
            }

            if (!success) return false;

            foreach(var record in records) record.Synced = true;
            await SaveRecordsAsync(records);
            return true;
        }

        public async Task LoadFromMarkdownAsync(string markdownText)
        {
            var lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var records = new List<SessionRecord>();
            var schemaCols = new List<string>();
            var parsedSchema = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("|") && !line.Contains("---|"))
                {
                    var cols = line.Split('|').Select(c => c.Trim()).ToArray();
                    if (!parsedSchema && line.Contains("Clock In") && line.Contains("Clock Out"))
                    {
                        // Parse headers
                        for (int i = 4; i < cols.Length - 2; i++) // Skip empty start/end, Clock In, Clock Out, Location... stop before Duration
                        {
                            var header = cols[i];
                            if (!string.IsNullOrEmpty(header) && header != "Duration")
                            {
                                schemaCols.Add(header);
                            }
                        }
                        parsedSchema = true;
                        continue;
                    }

                    if (parsedSchema && cols.Length >= 5) // At least basic columns
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

                            // Extract custom fields
                            for (int i = 0; i < schemaCols.Count; i++)
                            {
                                int colIndex = 4 + i;
                                if (colIndex < cols.Length - 1)
                                {
                                    record.CustomFields[schemaCols[i]] = cols[colIndex];
                                }
                            }
                            
                            records.Add(record);
                        }
                    }
                }
            }

            if (parsedSchema)
            {
                await SaveSchemaColumnsAsync(schemaCols);
            }
            await SaveRecordsAsync(records);
        }

        public async Task ClearAllRecordsAsync()
        {
            await _jsRuntime.InvokeVoidAsync("tapInterop.setItem", StorageKey, "[]");
        }
    }
}
