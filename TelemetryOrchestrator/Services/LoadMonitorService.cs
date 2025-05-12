using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly OrchestratorSettings _orchestratorSettings;
        private readonly IRegistryManager _registryManager;
        private readonly HttpService _httpService;
        private readonly IHubContext<LiveHub> _hubContext;

        private readonly Dictionary<SimulatorInfo, int> _simulatorReassignments;
        private readonly Dictionary<int, TelemetryDeviceInfo> _telemetryDevices;
        private readonly float _totalSystemRam;
        private readonly PortManager _portManager;

        public SortedSet<(float Load, int processId)> _deviceHeap;

        public LoadMonitorService(OrchestratorSettings settings, IRegistryManager registryManager, HttpService httpService, IHubContext<LiveHub> hubContext)
        {
            _hubContext = hubContext;
            _simulatorReassignments = new();
            _telemetryDevices = new();
            _deviceHeap = new(Comparer<(float Load, int processId)>.Create((a, b) => a.Load.CompareTo(b.Load)));
            _portManager = new();

            _totalSystemRam = MemoryInfo.GetTotalPhysicalMemory();
            _orchestratorSettings = settings;
            _registryManager = registryManager;
            _httpService = httpService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                List<int> devices = _registryManager.GetTelemetryDevices();

                if (devices.Count == 0)
                    await NewTelemetryDeviceProcces();

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        devices = _registryManager.GetTelemetryDevices();

                        //var tasks = devices.Select(device => BalanceSimulatorsDevices(device));
                        await BalanceSimulators();
                        //await Task.WhenAll(tasks);
                        await BroadcastOrchestratorUpdate();

                        //var tasks = devices.Select(device => CheckAndRebalanceDeviceLoadAsync(device));
                        //await Task.WhenAll(tasks);

                        //await RebalanceSimulators();

                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[LoadMonitor] Loop error: {ex}");
                    }

                    await Task.Delay(3000, stoppingToken);
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
                var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);

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
            var deviceInfo = _telemetryDevices[minDevice.processId];
            return (deviceInfo.DevicePort, deviceInfo.ListenerPort, minDevice.processId);
        }

        private async Task<float> CheckAndRebalanceDeviceLoadAsync(int processId)
        {
            Process process = Process.GetProcessById(processId);

            float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(process);
            float ramUsage = MemoryInfo.GetRamUsageForDevice(process);
            List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(processId);

            if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage || assignedSimulators.Count > _orchestratorSettings.MaxSimulatorsPerTD)
            {
                Console.WriteLine($"[Monitor] Device {processId} is overloaded, initiating rebalancing...");
                await RebalanceSimulatorsForDeviceAsync(processId);
            }

            return cpuUsage + ramUsage;
        }

        private async Task RebalanceSimulatorsForDeviceAsync(int overloadedDevice)
        {
            var devices = _registryManager.GetTelemetryDevices();
            var simulators = _registryManager.GetSimulatorsAssignedToDevice(overloadedDevice);

            var sortedDevices = new SortedSet<(float load, int deviceId)>(Comparer<(float, int)>.Create((x, y) =>
            {
                int cmp = x.Item1.CompareTo(y.Item1);
                return cmp != 0 ? cmp : x.Item2.CompareTo(y.Item2);
            }));

            foreach (var device in devices.Where(d => d != overloadedDevice))
            {
                float load = await MemoryInfo.GetDeviceLoad(device, _totalSystemRam);
                sortedDevices.Add((load, device));
            }

            int newDeviceId = await EnsureDeviceAvailability(sortedDevices);

            int simulatorsToMove = (int)Math.Floor(simulators.Count * 0.5);

            foreach (SimulatorInfo simulator in simulators.Take(simulatorsToMove))
            {
                if (sortedDevices.Any())
                {
                    var leastLoadedDevice = sortedDevices.Min;
                    int leastLoadedDeviceName = leastLoadedDevice.deviceId;

                    _registryManager.RegisterSimulator(simulator, leastLoadedDeviceName);
                    Console.WriteLine($"[LoadMonitor] Reassigned simulator {simulator} to {leastLoadedDeviceName}");
                    _simulatorReassignments[simulator] = leastLoadedDeviceName;

                    sortedDevices.Remove(leastLoadedDevice); // remove from the load ( log (n) )

                    float updatedLoad = await MemoryInfo.GetDeviceLoad(leastLoadedDeviceName, _totalSystemRam);
                    sortedDevices.Add((updatedLoad, leastLoadedDeviceName));
                }
            }

            await NotifySimulatorsOfNewAssignments();
        }

        private async Task BalanceSimulators()
        {
            List<int> telemetryDevices = _registryManager.GetTelemetryDevices();

            foreach (int telemetryProcessId in telemetryDevices)
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(telemetryProcessId);
                Process telemetryDeviceProcess = Process.GetProcessById(telemetryProcessId);

                float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(telemetryDeviceProcess);
                float ramUsage = MemoryInfo.GetRamUsageForDevice(telemetryDeviceProcess);

                if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage || assignedSimulators.Count > _orchestratorSettings.MaxSimulatorsPerTD)
                {
                    await Rebalance(telemetryProcessId);
                    break;
                }

            }
        }
        private async Task BalanceSimulatorsDevices(int deviceId)
        {
            Process process = Process.GetProcessById(deviceId);

            float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(process);
            float ramUsage = MemoryInfo.GetRamUsageForDevice(process);
            List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(deviceId);

            if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage || assignedSimulators.Count > _orchestratorSettings.MaxSimulatorsPerTD)
            {
                Console.WriteLine($"[Monitor] Device {deviceId} is overloaded, initiating rebalancing...");
                await Rebalance(deviceId);
            }

        }

        private async Task Rebalance(int overloadedDevice)
        {
            List<int> telemetryDevices = _registryManager.GetTelemetryDevices();
            List<SimulatorInfo> simulatorsOfOverloadDevice = _registryManager.GetSimulatorsAssignedToDevice(overloadedDevice);

            SortedSet<(float load, int deviceId)> sortedDevices = new SortedSet<(float load, int deviceId)>(Comparer<(float, int)>.Create((x, y) =>
            {
                int cmp = x.Item1.CompareTo(y.Item1);
                return cmp != 0 ? cmp : x.Item2.CompareTo(y.Item2);
            }));

            foreach (int deviceProcessId in telemetryDevices.Where(d => d != overloadedDevice))
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(deviceProcessId);
                Process deviceProcess = Process.GetProcessById(deviceProcessId);

                float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(deviceProcess);
                float ramUsage = MemoryInfo.GetRamUsageForDevice(deviceProcess);

                if (assignedSimulators.Count < _orchestratorSettings.MaxSimulatorsPerTD)
                {
                    float load = await MemoryInfo.GetDeviceLoad(deviceProcessId, _totalSystemRam);
                    sortedDevices.Add((load, deviceProcessId));
                }
            }

            int newDeviceId = await EnsureDeviceAvailability(sortedDevices);
            int simulatorsToMove = (int)Math.Floor(simulatorsOfOverloadDevice.Count * 0.5);

            foreach (SimulatorInfo simulator in simulatorsOfOverloadDevice.Take(simulatorsToMove))
            {
                if (sortedDevices.Any())
                {
                    var leastLoadedDevice = sortedDevices.Min;
                    int leastLoadedDeviceName = leastLoadedDevice.deviceId;

                    //_registryManager.RegisterSimulator(simulator, leastLoadedDeviceName);
                    Console.WriteLine($"[LoadMonitor] Reassigned simulator {simulator} to {leastLoadedDeviceName}");
                    _simulatorReassignments[simulator] = leastLoadedDeviceName;

                    sortedDevices.Remove(leastLoadedDevice); // remove from the load ( log (n) )

                    float updatedLoad = await MemoryInfo.GetDeviceLoad(leastLoadedDeviceName, _totalSystemRam);
                    sortedDevices.Add((updatedLoad, leastLoadedDeviceName));
                }
            }

            await NotifySimulatorsOfNewAssignments();
        }

        private async Task RebalanceSimulators()
        {
            List<int> devices = _registryManager.GetTelemetryDevices();

            var sortedDevices = new SortedSet<(float load, int deviceId)>(Comparer<(float, int)>.Create((x, y) =>
            {
                int cmp = x.Item1.CompareTo(y.Item1);
                return cmp != 0 ? cmp : x.Item2.CompareTo(y.Item2);
            }));


            foreach (int processId in devices)
            {
                Process device = Process.GetProcessById(processId);

                float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(device);
                float ramUsage = MemoryInfo.GetRamUsageForDevice(device);

                if (ramUsage <= 0.8 * _orchestratorSettings.MaxRamUsage || cpuUsage <= 0.8 * _orchestratorSettings.MaxCpuUsage)
                {
                    float load = await MemoryInfo.GetDeviceLoad(processId, _totalSystemRam);
                    sortedDevices.Add((load, processId));
                }
                else
                {
                    Console.WriteLine($"Device {device} is overloaded with CPU: {cpuUsage}% and RAM: {ramUsage}%");
                }
            }


            foreach (var device in devices)
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(device);
                if (assignedSimulators.Count > _orchestratorSettings.MaxSimulatorsPerTD)
                {
                    sortedDevices.RemoveWhere(d => d.deviceId == device);

                    int newDeviceId = await EnsureDeviceAvailability(sortedDevices);

                    int simulatorsToMove = (int)Math.Floor(assignedSimulators.Count * 0.5);

                    foreach (SimulatorInfo simulator in assignedSimulators.Take(simulatorsToMove))
                    {
                        var target = sortedDevices.FirstOrDefault(d => d.deviceId != device);
                        int targetDeviceId = target.deviceId > 0 ? target.deviceId : newDeviceId;

                        if (target.deviceId > 0 && targetDeviceId != device)
                        {
                            _registryManager.RegisterSimulator(simulator, target.deviceId);
                            _simulatorReassignments[simulator] = target.deviceId;

                            Console.WriteLine($"[LoadMonitor] Reassigned {simulator} to {target}");

                            sortedDevices.Remove(target);
                            float newLoad = await MemoryInfo.GetDeviceLoad(target.deviceId, _totalSystemRam);
                            sortedDevices.Add((newLoad, target.deviceId));

                        }
                    }
                }
            }

            await NotifySimulatorsOfNewAssignments();
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
                    FileName = _orchestratorSettings.TDServicePath,
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

                    Console.WriteLine($"Started process with ID: {process.Id} and Unique Identifier");
                    return process.Id;
                }
                else
                {
                    Console.WriteLine("Failed to start the Telemetry Device process.");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception when trying to create a new TelemetryDevice process: {ex}");
                return -1;
            }
        }
        private async Task NotifySimulatorsOfNewAssignments()
        {
            foreach (var simulator in _simulatorReassignments)
            {
                SimulatorInfo simulatorInfo = simulator.Key;
                int oldDeviceId = _telemetryDevices.FirstOrDefault(elemnt => _registryManager.GetSimulatorsAssignedToDevice(elemnt.Key).Contains(simulatorInfo)).Key;
                int newDeviceId = simulator.Value;

                //if (oldDeviceId == newDeviceId)
                //{
                //    newDeviceId = await NewTelemetryDeviceProcces();
                //}

                _registryManager.UnRegisterSimulator(simulatorInfo, oldDeviceId);
                _registryManager.RegisterSimulator(simulatorInfo, newDeviceId);

                TelemetryDeviceInfo targetTelemetryDevice = _telemetryDevices[newDeviceId];

                int uavNumber = simulatorInfo.UavNumber;
                int udpPort = targetTelemetryDevice.ListenerPort;


                OperationResult result = await _httpService.ReconfigureSimulatorEndpoint(uavNumber, udpPort, targetTelemetryDevice.DevicePort);

                if (result == OperationResult.Success)
                {
                    simulatorInfo.ControlEndPoint = udpPort;
                    await NotificationOfNewAssign(uavNumber, oldDeviceId, newDeviceId);
                    await BroadcastOrchestratorUpdate();

                    await UpdateDeviceHeap(newDeviceId);
                    await UpdateDeviceHeap(oldDeviceId);
                }
                else
                {
                    Console.WriteLine($"[LoadMonitor] Failed to notify simulator {uavNumber}");
                }

            }
            _simulatorReassignments.Clear();
        }
        private async Task<int> EnsureDeviceAvailability(SortedSet<(float load, int deviceId)> sortedDevices)
        {
            if (sortedDevices.Any())
            {
                var (minLoad, minDeviceId) = sortedDevices.Min;
                return minDeviceId;
            }

            Console.WriteLine("No available devices to move simulators to. Spinning up a new one...");

            int newDeviceId = await NewTelemetryDeviceProcces();
            if (newDeviceId != -1)
            {
                float load = await MemoryInfo.GetDeviceLoad(newDeviceId, _totalSystemRam);
                sortedDevices.Add((load, newDeviceId));
            }
            else
            {
                Console.WriteLine("[EnsureDeviceAvailability] Failed to start a new telemetry device.");
            }

            return newDeviceId;
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

                    Console.WriteLine($"update client for device id - {deviceId}");

                    List<SimulatorInfo> simulators = _registryManager.GetSimulatorsAssignedToDevice(deviceId);

                    orchestratorUpdate[deviceId] = new OrchestratorUpdateDto
                    {
                        Device = deviceInfo,
                        Simulators = simulators
                    };
                }

                await _hubContext.Clients.All.SendAsync("OrchestratorUpdate", orchestratorUpdate);
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

            await _hubContext.Clients.All.SendAsync("SimulatorAssignment", newAssign);
        }
        private async Task NotificationOfNewDevice(int newDeviceId)
        {
            await _hubContext.Clients.All.SendAsync("DeviceCreated", newDeviceId);
        }


    }
}
