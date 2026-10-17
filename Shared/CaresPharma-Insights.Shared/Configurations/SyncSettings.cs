using System;

namespace CaresPharma_Insights.Shared.Configurations
{
    public class SyncSettings
    {
        public const string SectionName = "SyncSettings";

        public string[] ExcludedTables { get; set; } = Array.Empty<string>();

        public string[] ExcludedColumnKeywords { get; set; } = Array.Empty<string>();

        public int SyncIntervalMinutes { get; set; } = 2;
    }
}
