using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Entities
{
    public class TelemetryDeviceInfo
    {
        public int DevicePort { get; set; }
        public int ListenerPort { get; set; }
        public Process Process { get; set; }
        //public float CurrentCpuUsage { get; set; }
        //public float CurrentRamUsage { get; set; }
    }
}
