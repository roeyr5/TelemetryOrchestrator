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
        private readonly Dictionary<SimulatorInfo, int> _simulatorReassignments;
        private readonly Dictionary<int, TelemetryDeviceInfo> _telemetryDevices;
        private readonly float _totalSystemRam;
        private readonly PortManager _portManager;
        private readonly HttpService _httpService;

        public SortedSet<(float Load, int processId)> _deviceHeap;
        private int _currentPort = 5000;
        private readonly object _portLock = new object();



        public LoadMonitorService(OrchestratorSettings settings, IRegistryManager registryManager , HttpService httpService)
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

                // Run parallel tasks for monitoring CPU and RAM usage
                var tasks = devices.Select(device => CheckAndRebalanceDeviceLoadAsync(device)).ToList();

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);

                // Rebalance simulators after monitoring all devices
                //RebalanceSimulators();

                await Task.Delay(5000, stoppingToken);  // Wait for 5 seconds before rechecking
            }
            Console.WriteLine("crahed");
        }
       
        //private async Task CreateTelemetryDeviceProcces()
        //{
        //    var (telemetryPort, simulatorPort) = _portManager.GetNextPorts();

        //    while (!IsPortAvailable(telemetryPort))
        //    {
        //        (telemetryPort, simulatorPort) = _portManager.GetNextPorts();
        //    }

        //    try
        //    {
        //        await Task.Run(async () =>
        //        {
        //            ProcessStartInfo startInfo = new ProcessStartInfo
        //            {
        //                FileName = _orchestratorSettings.TDServicePath,
        //                Arguments = $"--port={telemetryPort}",
        //                UseShellExecute = true,
        //                CreateNoWindow = false,
        //                WindowStyle = ProcessWindowStyle.Normal
        //            };

        //            Process process = Process.Start(startInfo);
        //            if (process != null)
        //            {
        //                TelemetryDeviceInfo deviceInfo = new TelemetryDeviceInfo
        //                {
        //                    DevicePort = telemetryPort,
        //                    ListenerPort = simulatorPort,
        //                    Process = process
        //                };

        //                _telemetryDevices[process.Id] = deviceInfo;
        //                _registryManager.RegisterTelemetryDevice(process.Id);
        //                await UpdateDeviceHeap(process.Id);
                        
        //                Console.WriteLine($"Started process with ID: {process.Id} and Unique Identifier");
        //                //return process.Id;
        //            }
        //            else
        //            {
        //                Console.WriteLine($"Failed to start the Telemetry Device process:");
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Got an exception when trying to create a new TelemetryDevice process: {ex}");
        //    }
        //}

        private bool IsPortAvailable(int port)
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
            float load = await GetDeviceLoadAsync(processId);
            _deviceHeap.Add((load, processId));
        }

        private async Task<float> GetDeviceLoadAsync(int processId)
        {
            Process process = Process.GetProcessById(processId);

            float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(process);
            float ramUsage = MemoryInfo.GetRamUsageForDevice(process);
            return cpuUsage + ramUsage;

        }
        public (int TelemetryPort, int SimulatorPort, int deviceId) GetMinLoadedPorts()
        {

            //if (_deviceHeap.Count == 0) return (0, 0);

            var minDevice = _deviceHeap.Min;
            var deviceInfo = _telemetryDevices[minDevice.processId];
            return (deviceInfo.DevicePort, deviceInfo.ListenerPort , minDevice.processId);

        }

        private async Task<float> CheckAndRebalanceDeviceLoadAsync(int processId)
        {
            Process process = Process.GetProcessById(processId);

            float cpuUsage = await MemoryInfo.GetCpuUsageForDeviceAsync(process);
            float ramUsage = MemoryInfo.GetRamUsageForDevice(process);

            if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage )
            {
                Console.WriteLine($"[Monitor] Device {processId} is overloaded, initiating rebalancing...");
                await RebalanceSimulatorsForDeviceAsync(processId);  // Rebalance simulators for this device
            }

            return cpuUsage + ramUsage;

        }

        private async Task RebalanceSimulatorsForDeviceAsync(int overloadedDevice)
        {
            var devices = _registryManager.GetTelemetryDevices();
            var simulators = _registryManager.GetSimulatorsAssignedToDevice(overloadedDevice);

            // Using SortedSet to simulate a min-heap (priority queue) by load
            var sortedDevices = new SortedSet<(float load, int deviceName)>(Comparer<(float, int)>.Create((x, y) => x.Item1.CompareTo(y.Item1)));

            foreach (var device in devices.Where(d => d != overloadedDevice))
            {
                float load = await MemoryInfo.GetDeviceLoad(device,_totalSystemRam);
                sortedDevices.Add((load, device));
            }

            if (!sortedDevices.Any())
            {
                Console.WriteLine("No available devices to move simulators to.");
                int newDeviceId = await NewTelemetryDeviceProcces();
                float load = await MemoryInfo.GetDeviceLoad(newDeviceId, _totalSystemRam);
                sortedDevices.Add((load,newDeviceId));
            }

            // Calculate how many simulators to move to reduce the load
            int simulatorsToMove = (int)Math.Floor(simulators.Count * 0.5); // Move only 50% of simulators, adjust this proportion as needed

            // Distribute simulators proportionally to devices based on their load
            foreach (var simulator in simulators.Take(simulatorsToMove))  // Only move the calculated portion of simulators
            {
                if (sortedDevices.Any())
                {
                    // Get the least loaded device
                    var leastLoadedDevice = sortedDevices.Min;
                    var leastLoadedDeviceName = leastLoadedDevice.deviceName;

                    // Register the simulator to the least loaded device
                    _registryManager.RegisterSimulator(simulator, leastLoadedDeviceName);
                    Console.WriteLine($"[LoadMonitor] Reassigned simulator {simulator} to {leastLoadedDeviceName}");
                    _simulatorReassignments[simulator] = leastLoadedDeviceName;


                    // Remove the least loaded device from the set (this operation is O(log n))
                    sortedDevices.Remove(leastLoadedDevice);

                    // Update the device load and re-add it to the heap
                    float updatedLoad = await MemoryInfo.GetDeviceLoad(leastLoadedDeviceName,_totalSystemRam);
                    sortedDevices.Add((updatedLoad, leastLoadedDeviceName));  // Re-insert the device with updated load
                }
            }

            // Optionally, notify simulators of new assignments if needed
            await NotifySimulatorsOfNewAssignments();
        }



        //private async Task RebalanceSimulators()
        //{
        //    var devices = _registryManager.GetTelemetryDevices();
        //    var simulators = _registryManager.GetAllSimulators();

        //    foreach (var device in devices)
        //    {
        //        // Get simulators assigned to this device
        //        var assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(device);
        //        if (assignedSimulators.Count > 5) // Arbitrary threshold
        //        {
        //            // Reassign simulators to less loaded devices (simplified logic)
        //            foreach (var simulator in assignedSimulators)
        //            {
        //                // Pick the next available device
        //                int targetDevice = devices.FirstOrDefault(d => d != device);
        //                if (targetDevice > 0)
        //                {
        //                    _registryManager.RegisterSimulator(simulator, targetDevice);
        //                    _simulatorReassignments[simulator] = targetDevice;

        //                    Console.WriteLine($"[LoadMonitor] Reassigned {simulator} to {targetDevice}");
        //                }
        //            }
        //        }
        //    }

        // Notify simulators of new assignments
        //await NotifySimulatorsOfNewAssignments();
        //}
        private async Task<int> NewTelemetryDeviceProcces()
        {
            var (telemetryPort, simulatorPort) = _portManager.GetNextPorts();

            // Keep trying until the telemetry port is available
            while (!IsPortAvailable(telemetryPort))
            {
                (telemetryPort, simulatorPort) = _portManager.GetNextPorts();
            }

            try
            {
                // Start the process with the provided start information
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _orchestratorSettings.TDServicePath,
                    Arguments = $"--port={telemetryPort}",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                // Start the process
                Process process = Process.Start(startInfo);
                if (process != null)
                {
                    // Successfully started the process, now register it
                    TelemetryDeviceInfo deviceInfo = new TelemetryDeviceInfo
                    {
                        DevicePort = telemetryPort,
                        ListenerPort = simulatorPort,
                        Process = process
                    };

                    // Save device info and register the device
                    _telemetryDevices[process.Id] = deviceInfo;
                    _registryManager.RegisterTelemetryDevice(process.Id);

                    // Update the device heap asynchronously
                    await UpdateDeviceHeap(process.Id);

                    // Log success and return the process ID
                    Console.WriteLine($"Started process with ID: {process.Id} and Unique Identifier");
                    return process.Id;
                }
                else
                {
                    // Log failure if the process did not start
                    Console.WriteLine("Failed to start the Telemetry Device process.");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                // Catch any exceptions and log the error
                Console.WriteLine($"Got an exception when trying to create a new TelemetryDevice process: {ex}");
                return -1;
            }
        }
        private async Task NotifySimulatorsOfNewAssignments()
        {
            foreach (var simulator in _simulatorReassignments)
            {
                SimulatorInfo simulatorInfo = simulator.Key;
                int deviceId = simulator.Value;

                TelemetryDeviceInfo targetTelemetryDevice = _telemetryDevices[deviceId];

                int uavNumber = simulatorInfo.UavNumber;
                int udpPort = targetTelemetryDevice.ListenerPort;

                OperationResult result = await _httpService.ReconfigureSimulatorEndpoint(deviceId,uavNumber, udpPort);

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

        private int GetNextPort()
        {
            lock (_portLock)
            {
                return _currentPort++;
            }
        }
    }
}
