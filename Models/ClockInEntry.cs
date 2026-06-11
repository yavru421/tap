using System;

namespace Tap.Client.Models
{
    public class ClockInEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? SelfieBase64 { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsSynced { get; set; }
        public bool SyncFailed { get; set; }
        public bool SyncSkip { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
