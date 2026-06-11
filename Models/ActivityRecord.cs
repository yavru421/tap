using System;

namespace Tap.Client.Models
{
    public class ActivityRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; } // "IN" or "OUT"
        public DateTime Timestamp { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public bool Synced { get; set; }
    }
}
