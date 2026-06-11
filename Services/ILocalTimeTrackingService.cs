using System.Collections.Generic;
using System.Threading.Tasks;
using Tap.Client.Models;

namespace Tap.Client.Services
{
    public interface ILocalTimeTrackingService
    {
        Task<List<SessionRecord>> GetRecordsAsync();
        Task<List<string>> GetSchemaColumnsAsync();
        Task ClockInAsync(string lat, string lng, Dictionary<string, string> customFields);
        Task ClockOutAsync();
        Task<bool> ExportToMarkdownAsync();
        Task ClearAllRecordsAsync();
        Task LoadFromMarkdownAsync(string markdownText);
    }
}
