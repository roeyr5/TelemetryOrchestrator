using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Extentions
{
    public class ChannelDTO
    {
        public int uavNumber { get; set; }
        public int? port { get; set; }
#nullable enable

    }
    public enum OperationResult
    {
        Success,
        AlreadyRequested,
        Failed
    }
}
