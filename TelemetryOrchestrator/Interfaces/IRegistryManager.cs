using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;

namespace TelemetryOrchestrator.Interfaces
{
    public interface IRegistryManager
    {
        // Registers a simulator to a specific telemetry device
        public void RegisterSimulator(SimulatorInfo simulatorId, string telemetryDeviceId);

        // Registers a new telemetry device
        public void RegisterTelemetryDevice(int telemetryDeviceId);

        // Retrieves simulators assigned to a specific telemetry device
        public List<SimulatorInfo> GetSimulatorsAssignedToDevice(string telemetryDeviceId);

        // Retrieves all simulators in the registry
        public List<SimulatorInfo> GetAllSimulators();

        // Retrieves all telemetry devices
        public List<string> GetTelemetryDevices();

        // Removes a simulator from a telemetry device
        public void RemoveSimulator(string simulatorId);

        // Removes a telemetry device and its simulators
        public void RemoveTelemetryDevice(string telemetryDeviceId);
    }
}
