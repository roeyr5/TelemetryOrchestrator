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

        public List<SimulatorInfo> GetAllSimulators()
        {
            return _telemetryDeviceSimulators.Values.SelectMany(s => s).ToList();
        }
        public List<int> GetTelemetryDevices()
        {
            return _telemetryDevices.ToList();
        }
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
                throw new InvalidOperationException("device not registerd");
            }

            if (!_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId))
            {
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<SimulatorInfo>();
            }

            _telemetryDeviceSimulators[telemetryDeviceId].Add(simulatorId);
        }

        public void RegisterTelemetryDevice(int telemetryDeviceId)
        {
            if (!_telemetryDevices.Contains(telemetryDeviceId))
            {
                _telemetryDevices.Add(telemetryDeviceId);
                _telemetryDeviceSimulators[telemetryDeviceId] = new List<SimulatorInfo>(); // Initialize with no simulators
            }
        }


        public void RemoveTelemetryDevice(int telemetryDeviceId)
        {
            if (_telemetryDevices.Contains(telemetryDeviceId))
            {
                if (!_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId) || _telemetryDeviceSimulators[telemetryDeviceId].Count == 0)
                {
                    _telemetryDevices.Remove(telemetryDeviceId);
                    _telemetryDeviceSimulators.Remove(telemetryDeviceId);

                    TerminateTelemetryDeviceProcess(telemetryDeviceId);
                }
              
            }
        }

        public void UnRegisterSimulator(SimulatorInfo simulator, int telemetryDeviceId)
        {
            if (_telemetryDeviceSimulators.ContainsKey(telemetryDeviceId))
            {
                _telemetryDeviceSimulators[telemetryDeviceId].Remove(simulator);
            }
        }

        public void UpdateSimulatorAssignment(SimulatorInfo simulator, int fromDeviceId, int toDeviceId)
        {
            UnRegisterSimulator(simulator, fromDeviceId);
            RegisterSimulator(simulator, toDeviceId);
        }

        private static void TerminateTelemetryDeviceProcess(int telemetryDeviceId)
        {
            try
            {
                Process process = Process.GetProcessById(telemetryDeviceId);
                process.Kill();
            }
            catch (ArgumentException)
            {
            }
        }

    }
}
