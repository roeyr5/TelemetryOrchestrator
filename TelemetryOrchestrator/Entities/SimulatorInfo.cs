using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Entities
{
    public class SimulatorInfo
    {
        public SimulatorInfo(int uavNumber , int listeningPort)
        {
            UavNumber = uavNumber;
            ControlEndPoint = listeningPort;
        }
        public int UavNumber { get; set; }
        public int ControlEndPoint { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is SimulatorInfo other)
            {
                return this.UavNumber == other.UavNumber &&
                       this.ControlEndPoint == other.ControlEndPoint;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(UavNumber, ControlEndPoint);
        }
    }
}
