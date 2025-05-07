using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Entities
{
    public class TelemetryDeviceInfo
    {
        public int DevicePort { get; set; }
        public int ListenerPort { get; set; }

        [JsonIgnore]
        public Process Process { get; set; }
        //public float CurrentCpuUsage { get; set; }
        //public float CurrentRamUsage { get; set; }
    }
    public class OrchestratorUpdateDto
    {
        public TelemetryDeviceInfo Device { get; set; }
        public List<SimulatorInfo> Simulators { get; set; }
    }
    public class SimulatorReassign
    {
        public int UavNumber { get; set; }
        public int OldDeviceId { get; set; }
        public int NewDeviceId { get; set; }
    }
}
