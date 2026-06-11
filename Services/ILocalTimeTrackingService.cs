using System.Collections.Generic;
using System.Threading.Tasks;
using Tap.Client.Models;

namespace Tap.Client.Services
{
    public interface ILocalTimeTrackingService
    {
        Task<List<ActivityRecord>> GetRecordsAsync();
        Task AddRecordAsync(ActivityRecord record);
        Task<bool> ExportToMarkdownAsync();
        Task ClearAllRecordsAsync();
    }
}
