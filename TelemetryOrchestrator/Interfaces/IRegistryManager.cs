using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;

namespace TelemetryOrchestrator.Interfaces
{
    public interface IRegistryManager
    {
        public void RegisterSimulator(SimulatorInfo simulatorId, int telemetryDeviceId);

        public void RegisterTelemetryDevice(int telemetryDeviceId);

        public List<SimulatorInfo> GetSimulatorsAssignedToDevice(int telemetryDeviceId);

        public List<SimulatorInfo> GetAllSimulators();

        public List<int> GetTelemetryDevices();

        public void UnRegisterSimulator(SimulatorInfo simulatorId, int telemetryDeviceId);

        public void RemoveTelemetryDevice(int telemetryDeviceId);
    }
}
