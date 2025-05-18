using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Entities
{
    public class AutoScalerSettings
    {
        public string TDServicePath { get; set; }
        public int MaxSimulatorsPerTD { get; set; }
        public int MaxCpuUsage { get; set; }
        public int MaxRamUsage { get; set; }
    }
}
