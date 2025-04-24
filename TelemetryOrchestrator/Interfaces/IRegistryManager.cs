using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Interfaces
{
    public interface IRegistryManager
    {
        // Registers a simulator to a specific telemetry device
        void RegisterSimulator(string simulatorId, string telemetryDeviceId);

        // Registers a new telemetry device
        void RegisterTelemetryDevice(string telemetryDeviceId);

        // Retrieves simulators assigned to a specific telemetry device
        List<string> GetSimulatorsAssignedToDevice(string telemetryDeviceId);

        // Retrieves all simulators in the registry
        List<string> GetAllSimulators();

        // Retrieves all telemetry devices
        List<string> GetTelemetryDevices();

        // Removes a simulator from a telemetry device
        void RemoveSimulator(string simulatorId);

        // Removes a telemetry device and its simulators
        void RemoveTelemetryDevice(string telemetryDeviceId);
    }
}
