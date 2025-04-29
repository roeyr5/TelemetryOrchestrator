using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;
using TelemetryOrchestrator.Extentions;
using TelemetryOrchestrator.Interfaces;
using TelemetryOrchestrator.Services.Http_Requests;

namespace TelemetryOrchestrator.Services
{
    public class LoadMonitorService : BackgroundService
    {
        private readonly OrchestratorSettings _orchestratorSettings;
        private readonly IRegistryManager _registryManager;
        private readonly HttpService _httpService;

        private readonly Dictionary<SimulatorInfo, int> _simulatorReassignments;
        private readonly Dictionary<int, TelemetryDeviceInfo> _telemetryDevices;
        private readonly float _totalSystemRam;
        private readonly PortManager _portManager;

        public SortedSet<(float Load, int processId)> _deviceHeap;



        public LoadMonitorService(OrchestratorSettings settings, IRegistryManager registryManager, HttpService httpService)
        {
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
            while (!stoppingToken.IsCancellationRequested)
            {
                // Get all telemetry devices
                var devices = _registryManager.GetTelemetryDevices();
                if (devices.Count == 0)
                {
                    await NewTelemetryDeviceProcces();
                }

                var tasks = devices.Select(device => CheckAndRebalanceDeviceLoadAsync(device)).ToList();

                await Task.WhenAll(tasks);

                await RebalanceSimulators();

                await Task.Delay(5000, stoppingToken);
            }
            Console.WriteLine("exception");
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

            if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage)
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

            foreach (var simulator in simulators.Take(simulatorsToMove))
            {
                if (sortedDevices.Any())
                {
                    var leastLoadedDevice = sortedDevices.Min;
                    var leastLoadedDeviceName = leastLoadedDevice.deviceId;

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

                float cpuPercentage = cpuUsage / _orchestratorSettings.MaxCpuUsage * 100;
                float ramPercentage = ramUsage / _orchestratorSettings.MaxRamUsage * 100;

                if (cpuPercentage <= 0.8 * _orchestratorSettings.MaxCpuUsage || ramPercentage <= 0.8 * _orchestratorSettings.MaxRamUsage)
                {
                    float load = cpuUsage + ramUsage;
                    sortedDevices.Add((load, processId));
                }
                else
                {
                    Console.WriteLine($"Device {device} is overloaded with CPU: {cpuPercentage}% and RAM: {ramPercentage}%");
                }
            }

            int newDeviceId = await EnsureDeviceAvailability(sortedDevices);

            foreach (var device in devices)
            {
                List<SimulatorInfo> assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(device);
                if (assignedSimulators.Count > _orchestratorSettings.MaxSimulatorsPerTD)
                {
                    int simulatorsToMove = (int)Math.Floor(assignedSimulators.Count * 0.5);

                    foreach (SimulatorInfo simulator in assignedSimulators.Take(simulatorsToMove))
                    {
                        var target = sortedDevices.FirstOrDefault(d => d.deviceId != device);
                        if (target.deviceId > 0)
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
                    UseShellExecute = true,
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
                int oldDeviceId = _telemetryDevices.FirstOrDefault(kv => _registryManager.GetSimulatorsAssignedToDevice(kv.Key).Contains(simulatorInfo)).Key;
                int newDeviceId = simulator.Value;

                if (oldDeviceId != 0 && oldDeviceId != newDeviceId)
                {
                    _registryManager.UnRegisterSimulator(simulatorInfo, oldDeviceId);
                }

                TelemetryDeviceInfo targetTelemetryDevice = _telemetryDevices[newDeviceId];

                int uavNumber = simulatorInfo.UavNumber;
                int udpPort = targetTelemetryDevice.ListenerPort;

                OperationResult result = await _httpService.ReconfigureSimulatorEndpoint(newDeviceId, uavNumber, udpPort);

                if (result == OperationResult.Success)
                {
                    simulatorInfo.ControlEndPoint = udpPort;
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
            if (!sortedDevices.Any())
            {
                Console.WriteLine("No available devices to move simulators to.");

                int newDeviceId = await NewTelemetryDeviceProcces();
                if (newDeviceId != -1)
                {
                    float load = await MemoryInfo.GetDeviceLoad(newDeviceId, _totalSystemRam);
                    sortedDevices.Add((load, newDeviceId));
                }

                return newDeviceId;
            }

            return -1;
        }

    }
}
