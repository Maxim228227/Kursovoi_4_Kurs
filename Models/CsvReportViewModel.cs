using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class CsvReportViewModel
    {
        public string CsvFileName { get; set; }
        public List<string> CsvLines { get; set; } = new List<string>();
        public string ReportText { get; set; } = string.Empty;
    }
}
