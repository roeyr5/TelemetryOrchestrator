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
        private readonly Dictionary<int, List<SimulatorInfo>> _telemetryDeviceSimulators;
        private readonly HashSet<int> _telemetryDevices;

        public RegistryManager()
        {
            _telemetryDeviceSimulators = new();
            _telemetryDevices = new();
            //_ = new LoadMonitorService(settings.Value, this);
        }

        // Retrieves all simulators in the registry
        public List<SimulatorInfo> GetAllSimulators()
        {
            return _telemetryDeviceSimulators.Values.SelectMany(s => s).ToList();
        }

        // Retrieves all telemetry devices
        public List<int> GetTelemetryDevices()
        {
            return _telemetryDevices.ToList();
        }

        // Retrieves simulators assigned to a specific telemetry device
        public List<SimulatorInfo> GetSimulatorsAssignedToDevice(int telemetryDeviceId)
        {
            return _telemetryDeviceSimulators.ContainsKey(telemetryDeviceId)
                ? _telemetryDeviceSimulators[telemetryDeviceId]
                : new List<SimulatorInfo>();
        }

        public void RegisterSimulator(SimulatorInfo simulatorId, int telemetryDeviceId)
        {
            if (!_telemetryDevices.Contains(telemetryDeviceId))
            {
                throw new InvalidOperationException("Telemetry device not registered.");
            }

            if (!_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId))
            {
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<SimulatorInfo>();
            }

            _telemetryDeviceSimulators[telemetryDeviceId].Add(simulatorId);
            Console.WriteLine($"Simulator {simulatorId} registered to {telemetryDeviceId}");
        }

        public void RegisterTelemetryDevice(int telemetryDeviceId)
        {
            if (!_telemetryDevices.Contains(telemetryDeviceId))
            {
                _telemetryDevices.Add(telemetryDeviceId);
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<SimulatorInfo>(); // Initialize with no simulators
                Console.WriteLine($"Telemetry device {telemetryDeviceId} registered.");
            }
        }


        // Removes a simulator from a telemetry device
        public void RemoveSimulator(SimulatorInfo simulatorId)
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


        public void RemoveTelemetryDevice(int telemetryDeviceId)
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

        public void UnRegisterSimulator(SimulatorInfo simulatorId, int telemetryDeviceId)
        {
            if (_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId))
            {
                _telemetryDeviceSimulators[telemetryDeviceId].Remove(simulatorId);
                Console.WriteLine($"Simulator {simulatorId} unregistered from {telemetryDeviceId}");
            }
        }


        private static void TerminateTelemetryDeviceProcess(int telemetryDeviceId)
        {
            try
            {
                Process process = Process.GetProcessById(telemetryDeviceId);
                process.Kill();
                Console.WriteLine($"Terminated process for Telemetry Device: {telemetryDeviceId}");
            }
            catch (ArgumentException)
            {
                Console.WriteLine($"No process found for Telemetry Device: {telemetryDeviceId}. It may not be running.");
            }
        }

    }
}
