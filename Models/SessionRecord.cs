using System;

namespace Tap.Client.Models
{
    public class SessionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime InTime { get; set; }
        public DateTime? OutTime { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public bool Synced { get; set; }
        
        public Dictionary<string, string> CustomFields { get; set; } = new();

        public TimeSpan? Duration => OutTime.HasValue ? OutTime.Value - InTime : null;
        public bool IsActive => !OutTime.HasValue;
    }
}
