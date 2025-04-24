using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TelemetryOrchestrator.Entities;
using TelemetryOrchestrator.Interfaces;

namespace TelemetryOrchestrator.Services
{
    public class RegistryManager : IRegistryManager
    {
        // This dictionary holds the telemetry devices and their assigned simulators.
        private readonly Dictionary<string, List<string>> _telemetryDeviceSimulators;

        // HashSet to hold registered telemetry devices
        private readonly HashSet<string> _telemetryDevices;

        public RegistryManager(IOptions<OrchestratorSettings> settings)
        {
            _telemetryDeviceSimulators = new();
            _telemetryDevices = new();
            _ = new LoadMonitorService(settings.Value, this);
        }

        // Retrieves all simulators in the registry
        public List<string> GetAllSimulators()
        {
            return _telemetryDeviceSimulators.Values.SelectMany(s => s).ToList();
        }

        // Retrieves all telemetry devices
        public List<string> GetTelemetryDevices()
        {
            return _telemetryDevices.ToList();
        }

        // Retrieves simulators assigned to a specific telemetry device
        public List<string> GetSimulatorsAssignedToDevice(string telemetryDeviceId)
        {
            return _telemetryDeviceSimulators.ContainsKey(telemetryDeviceId)
                ? _telemetryDeviceSimulators[telemetryDeviceId]
                : new List<string>();
        }

        public void RegisterSimulator(string simulatorId, string telemetryDeviceId)
        {
            if (!_telemetryDevices.Contains(telemetryDeviceId))
            {
                throw new InvalidOperationException("Telemetry device not registered.");
            }

            if (!_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId))
            {
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<string>();
            }

            _telemetryDeviceSimulators[telemetryDeviceId].Add(simulatorId);
            Console.WriteLine($"Simulator {simulatorId} registered to {telemetryDeviceId}");
        }

        public void RegisterTelemetryDevice(string telemetryDeviceId)
        {
            if (!_telemetryDevices.Contains(telemetryDeviceId))
            {
                _telemetryDevices.Add(telemetryDeviceId);
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<string>(); // Initialize with no simulators
                Console.WriteLine($"Telemetry device {telemetryDeviceId} registered.");
            }
        }

        // Removes a simulator from a telemetry device
        public void RemoveSimulator(string simulatorId)
        {
            foreach (var device in _telemetryDeviceSimulators)
            {
                if (device.Value.Contains(simulatorId))
                {
                    device.Value.Remove(simulatorId);
                    Console.WriteLine($"Removed simulator {simulatorId} from {device.Key}");
                    return;
                }
            }

            Console.WriteLine($"Simulator {simulatorId} not found.");
        }

        public void RemoveTelemetryDevice(string telemetryDeviceId)
        {
            if (_telemetryDevices.Contains(telemetryDeviceId))
            {
                // Check if the device has any simulators assigned to it
                if (!_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId) || _telemetryDeviceSimulators[telemetryDeviceId].Count == 0)
                {
                    // No simulators assigned, remove the telemetry device from the registry
                    _telemetryDevices.Remove(telemetryDeviceId);
                    _telemetryDeviceSimulators.Remove(telemetryDeviceId);

                    // Attempt to terminate the process associated with the telemetry device
                    TerminateTelemetryDeviceProcess(telemetryDeviceId);

                    Console.WriteLine($"Telemetry device {telemetryDeviceId} has no simulators and has been removed.");
                }
                else
                {
                    Console.WriteLine($"Telemetry device {telemetryDeviceId} still has simulators assigned and cannot be removed.");
                }
            }
            else
            {
                Console.WriteLine($"Telemetry device {telemetryDeviceId} not found.");
            }
        }

        private static void TerminateTelemetryDeviceProcess(string telemetryDeviceId)
        {
            // Attempt to find the running process by name (same as the telemetry device ID)
            var processes = Process.GetProcessesByName(telemetryDeviceId);

            if (processes.Length > 0)
            {
                // Get the first matching process and kill it
                var process = processes[0];
                process.Kill();
                Console.WriteLine($"Terminated process for Telemetry Device: {telemetryDeviceId}");
            }
            else
            {
                Console.WriteLine($"No process found for Telemetry Device: {telemetryDeviceId}. It may not be running.");
            }
        }
    }
}
