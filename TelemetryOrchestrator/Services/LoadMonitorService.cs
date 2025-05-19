using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;
using TelemetryOrchestrator.Extentions;
using TelemetryOrchestrator.Hubs;
using TelemetryOrchestrator.Interfaces;
using TelemetryOrchestrator.Services.Http_Requests;

namespace TelemetryOrchestrator.Services
{
    public class LoadMonitorService : BackgroundService
    {
        private const string ORCHESTRATOR_UPDATE = "OrchestratorUpdate";
        private const string SIMULATOR_ASSIGNMENT = "SimulatorAssignment";
        private const string DEVICE_CREATED = "DeviceCreated";

        private readonly AutoScalerSettings _autoScalerSettings;
        private readonly IRegistryManager _registryManager;
        private readonly HttpService _httpService;
        private readonly IHubContext<LiveHub> _hubContext;

        private readonly Dictionary<SimulatorInfo, int> _simulatorReassignments;
        private readonly Dictionary<int, TelemetryDeviceInfo> _telemetryDevices;
        private readonly SortedSet<(float load, int deviceId)> _sortedDevices;
        private readonly float _totalSystemRam;
        private readonly PortManager _portManager;

        public SortedSet<(float Load, int processId)> _deviceHeap;

        public LoadMonitorService(AutoScalerSettings settings, IRegistryManager registryManager, HttpService httpService, IHubContext<LiveHub> hubContext)
        {
            _hubContext = hubContext;
            _simulatorReassignments = new();
            _telemetryDevices = new();
            _deviceHeap = new(Comparer<(float load, int deviceId)>.Create((a, b) => { int cmp = a.load.CompareTo(b.load); return cmp != 0 ? cmp : a.deviceId.CompareTo(b.deviceId); }));
            _sortedDevices = new(Comparer<(float load, int deviceId)>.Create((a, b) => { int cmp = a.load.CompareTo(b.load); return cmp != 0 ? cmp : a.deviceId.CompareTo(b.deviceId); }));
            _portManager = new();

            _totalSystemRam = MemoryInfo.GetTotalPhysicalMemory();
            _autoScalerSettings = settings;
            _registryManager = registryManager;
            _httpService = httpService;

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                SetAutoScalerLimits();
                List<int> devices = _registryManager.GetTelemetryDevices();

                if (devices.Count == 0)
                    await NewTelemetryDeviceProcces();

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckBalanceDevices();
                        await BroadcastOrchestratorUpdate();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"error: {ex}");
                    }

                    await Task.Delay(2000, stoppingToken);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("exception : " + e);
            }
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                TcpListener tcpListener = new(System.Net.IPAddress.Loopback, port);

                tcpListener.Start();
                tcpListener.Stop();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task UpdateDeviceHeap(int processId)
        {
            float load = await MemoryInfo.GetDeviceLoad(processId, _totalSystemRam);
            _deviceHeap.Add((load, processId));
        }

        public (int TelemetryPort, int SimulatorPort, int deviceId) GetMinLoadedPorts()
        {
            var minDevice = _deviceHeap.Min;
            TelemetryDeviceInfo deviceInfo = _telemetryDevices[minDevice.processId];
            return (deviceInfo.DevicePort, deviceInfo.ListenerPort, minDevice.processId);
        }

        private async Task CheckBalanceDevices()
        {
            List<int> telemetryDevices = _registryManager.GetTelemetryDevices();

            foreach (int deviceProcessId in telemetryDevices)
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(deviceProcessId);
                Process telemetryDeviceProcess = Process.GetProcessById(deviceProcessId);

                float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(telemetryDeviceProcess);
                float ramUsage = MemoryInfo.GetRamUsageForDevice(telemetryDeviceProcess);

                if (cpuUsage > _autoScalerSettings.MaxCpuUsage || ramUsage > _autoScalerSettings.MaxRamUsage || assignedSimulators.Count > _autoScalerSettings.MaxSimulatorsPerTD)
                {
                    await RebalanceDevices(deviceProcessId);
                    break;
                }

            }
        }
        private async Task RebalanceDevices(int overloadedDeviceId)
        {
            List<int> telemetryDevices = _registryManager.GetTelemetryDevices();
            await BuildTargetDevice(telemetryDevices, overloadedDeviceId);
            List<SimulatorInfo> simulatorsOfOverloadDevice = _registryManager.GetSimulatorsAssignedToDevice(overloadedDeviceId);

            await ReassignSimulators(simulatorsOfOverloadDevice);

        }
        private async Task ReassignSimulators(List<SimulatorInfo> simulatorsOfOverloadDevice)
        {
            int simulatorsToMove = (int)Math.Floor(simulatorsOfOverloadDevice.Count * 0.5) - 1;

            foreach (SimulatorInfo simulator in simulatorsOfOverloadDevice.Take(simulatorsToMove))
            {
                if (_sortedDevices.Any())
                {
                    var leastLoadedDevice = _sortedDevices.Min;
                    int leastLoadedDeviceName = leastLoadedDevice.deviceId;

                    _simulatorReassignments[simulator] = leastLoadedDeviceName;

                    _sortedDevices.Remove(leastLoadedDevice); // remove from the load ( log (n) )

                    float updatedLoad = await MemoryInfo.GetDeviceLoad(leastLoadedDeviceName, _totalSystemRam);
                    _sortedDevices.Add((updatedLoad, leastLoadedDeviceName));

                }
            }

            await NotifySimulatorsOfNewAssignments();
        }
        private async Task BuildTargetDevice(List<int> telemetryDevices, int overloadedDeviceId)
        {
            _sortedDevices.Clear();

            foreach (int deviceId in telemetryDevices.Where(device => device != overloadedDeviceId))
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(deviceId);
                Process deviceProcess = Process.GetProcessById(deviceId);

                float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(deviceProcess);
                float ramUsage = MemoryInfo.GetRamUsageForDevice(deviceProcess);

                if (assignedSimulators.Count < _autoScalerSettings.MaxSimulatorsPerTD && ramUsage <= 0.85 * _autoScalerSettings.MaxRamUsage && cpuUsage <= 0.85 * _autoScalerSettings.MaxCpuUsage)
                {
                    float load = MemoryInfo.CalculateDeviceLoad(ramUsage, deviceId, _totalSystemRam);
                    _sortedDevices.Add((load, deviceId));
                }
            }
            await EnsureDeviceAvailability();
        }
        private async Task<int> NewTelemetryDeviceProcces()
        {
            var (telemetryPort, simulatorPort) = _portManager.GetNextPorts();

            while (!IsPortAvailable(telemetryPort))
            {
                (telemetryPort, simulatorPort) = _portManager.GetNextPorts();
            }

            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = _autoScalerSettings.TDServicePath,
                    Arguments = $"--port={telemetryPort}",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process process = Process.Start(startInfo);
                if (process != null)
                {
                    TelemetryDeviceInfo deviceInfo = new()
                    {
                        DevicePort = telemetryPort,
                        ListenerPort = simulatorPort,
                        Process = process
                    };

                    _telemetryDevices[process.Id] = deviceInfo;
                    _registryManager.RegisterTelemetryDevice(process.Id);

                    await UpdateDeviceHeap(process.Id);
                    await NotificationOfNewDevice(process.Id);

                    return process.Id;
                }
                else
                {
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"exception message : {ex}");
                return -1;
            }
        }
        private async Task NotifySimulatorsOfNewAssignments()
        {
            foreach (var simulator in _simulatorReassignments)
            {
                SimulatorInfo simulatorInfo = simulator.Key;
                int newDeviceId = simulator.Value;
                int oldDeviceId = _telemetryDevices.FirstOrDefault(element => _registryManager.GetSimulatorsAssignedToDevice(element.Key).Contains(simulatorInfo)).Key;

                TelemetryDeviceInfo targetTelemetryDevice = _telemetryDevices[newDeviceId];
                _registryManager.UpdateSimulatorAssignment(simulatorInfo, oldDeviceId, newDeviceId);

                await HandleSimulatorReassignment(simulatorInfo, oldDeviceId, newDeviceId, targetTelemetryDevice);
            }
            _simulatorReassignments.Clear();

        }
        private async Task HandleSimulatorReassignment(SimulatorInfo simulator, int oldDeviceId, int newDeviceId, TelemetryDeviceInfo targetTelemetryDevice)
        {
            int uavNumber = simulator.UavNumber;
            int udpPort = targetTelemetryDevice.ListenerPort;
            int devicePort = targetTelemetryDevice.DevicePort;

            OperationResult result = await _httpService.ReconfigureSimulatorEndpoint(uavNumber, udpPort, devicePort);

            if (result == OperationResult.Success)
            {
                simulator.ControlEndPoint = udpPort;

                await NotificationOfNewAssign(uavNumber, oldDeviceId, newDeviceId);
                await BroadcastOrchestratorUpdate();

                await Task.WhenAll(
                    UpdateDeviceHeap(newDeviceId),
                    UpdateDeviceHeap(oldDeviceId)
                );
            }
            else
            {
                Console.WriteLine($"Failed to reassign simulator {uavNumber}");
            }
        }
        private async Task<int> EnsureDeviceAvailability()
        {
            if (_sortedDevices.Any())
            {
                var (minLoad, minDeviceId) = _sortedDevices.Min;
                return minDeviceId;
            }

            int newDeviceId = await NewTelemetryDeviceProcces();

            if (newDeviceId != -1)
            {
                float load = await MemoryInfo.GetDeviceLoad(newDeviceId, _totalSystemRam);
                _sortedDevices.Add((load, newDeviceId));
            }

            return newDeviceId;
        }

        private void SetAutoScalerLimits()
        {
            _autoScalerSettings.MaxRamUsage = (int)(_totalSystemRam * 0.0125);

            int coreCount = Environment.ProcessorCount;
            float percentOfOneCore = 0.25f;
            int maxCpuUsage = (int)(100f / coreCount * percentOfOneCore);

            _autoScalerSettings.MaxCpuUsage = maxCpuUsage;

        }


        private async Task BroadcastOrchestratorUpdate()
        {
            try
            {
                Dictionary<int, OrchestratorUpdateDto> orchestratorUpdate = new();

                foreach (var element in _telemetryDevices)
                {
                    int deviceId = element.Key;
                    TelemetryDeviceInfo deviceInfo = element.Value;
                    List<SimulatorInfo> simulators = _registryManager.GetSimulatorsAssignedToDevice(deviceId);

                    orchestratorUpdate[deviceId] = new OrchestratorUpdateDto
                    {
                        Device = deviceInfo,
                        Simulators = simulators,

                    };
                }

                await _hubContext.Clients.All.SendAsync(ORCHESTRATOR_UPDATE, orchestratorUpdate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error is : {ex}");
            }

        }
        private async Task NotificationOfNewAssign(int uavNumber, int oldDeviceId, int newDeviceId)
        {
            SimulatorReassign newAssign = new()
            {
                UavNumber = uavNumber,
                OldDeviceId = oldDeviceId,
                NewDeviceId = newDeviceId
            };

            await _hubContext.Clients.All.SendAsync(SIMULATOR_ASSIGNMENT, newAssign);
        }
        private async Task NotificationOfNewDevice(int newDeviceId)
        {
            await _hubContext.Clients.All.SendAsync(DEVICE_CREATED, newDeviceId);
        }


    }
}
