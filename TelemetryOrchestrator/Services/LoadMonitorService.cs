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

namespace TelemetryOrchestrator.Services
{
    public class LoadMonitorService : BackgroundService
    {
        private readonly OrchestratorSettings _orchestratorSettings;
        private readonly IRegistryManager _registryManager;
        private readonly Dictionary<string, string> _simulatorReassignments;
        private readonly Dictionary<string, Process> _processes;
        private readonly float _totalSystemRam;

        public LoadMonitorService(OrchestratorSettings settings, IRegistryManager registryManager)
        {
            _simulatorReassignments = new();
            _processes = new();
            _totalSystemRam = MemoryInfo.GetTotalSystemRam();
            _orchestratorSettings = settings;
            _registryManager = registryManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Get all telemetry devices
                var devices = _registryManager.GetTelemetryDevices();
                if (devices.Count == 0)
                {
                    await CreateTelemetryDeviceProcces();
                }

                // Run parallel tasks for monitoring CPU and RAM usage
                var tasks = devices.Select(device => MonitorDeviceAsync(device)).ToList();

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);

                // Rebalance simulators after monitoring all devices
                RebalanceSimulators();

                await Task.Delay(5000, stoppingToken);  // Wait for 5 seconds before rechecking
            }
        }

        private async Task CreateTelemetryDeviceProcces()
        {
            try
            {
                // Start the Telemetry Device process in a non-blocking way
                await Task.Run(() =>
                {
                    string newTelemetryDeviceId = Guid.NewGuid().ToString();
                    var startInfo = new ProcessStartInfo("dotnet", $"TelemetryDeviceApp.dll {newTelemetryDeviceId}")
                    {
                        UseShellExecute = false, // Optional: Prevents opening a window for console apps
                    };

                    Process process = Process.Start(startInfo);
                    //var process = Process.Start($"{_orchestratorSettings.TDServicePath}", $"TelemetryDeviceApp.dll {newTelemetryDeviceId}");
                    if (process == null)
                    {
                        Console.WriteLine($"Failed to start the Telemetry Device process: {newTelemetryDeviceId}");
                    }
                    else
                    {
                        //process.WaitForExit(); // Wait for the process to finish (if necessary)
                        _processes[newTelemetryDeviceId] = process;
                        _registryManager.RegisterTelemetryDevice(newTelemetryDeviceId);
                        Console.WriteLine($"Started process with ID: {process.Id} and Unique Identifier: {newTelemetryDeviceId}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got an exception when trying to create a new TelemetryDevice process: {ex}");
            }
        }

        private async Task MonitorDeviceAsync(string device)
        {
            // Use Process.GetProcessesByName to find the process for the specific Telemetry Device
            var processes = Process.GetProcessesByName(device);  // Assumes device name matches process name

            if (processes.Length > 0)
            {
                Process process = processes[0];
                float cpuUsage = GetCpuUsageForDevice(process);
                float ramUsage = GetRamUsageForDevice(process);

                //Console.WriteLine($"[Monitor] Device: {device} CPU: {cpuUsage:F1}% RAM: {ramUsage:F1}%");

                // If the device is overloaded, rebalance simulators
                if (cpuUsage > _orchestratorSettings.MaxCpuUsage || ramUsage > _orchestratorSettings.MaxRamUsage)
                {
                    Console.WriteLine($"[Monitor] Device {device} is overloaded, initiating rebalancing...");
                    await RebalanceSimulatorsForDeviceAsync(device);  // Rebalance simulators for this device
                }
            }
        }

        private async Task RebalanceSimulatorsForDeviceAsync(string overloadedDevice)
        {
            var devices = _registryManager.GetTelemetryDevices();
            var simulators = _registryManager.GetSimulatorsAssignedToDevice(overloadedDevice);

            // Using SortedSet to simulate a min-heap (priority queue) by load
            var sortedDevices = new SortedSet<(float load, string deviceName)>(Comparer<(float, string)>.Create((x, y) => x.Item1.CompareTo(y.Item1)));

            foreach (var device in devices.Where(d => d != overloadedDevice))
            {
                var load = GetDeviceLoad(device);
                sortedDevices.Add((load, device));
            }

            if (!sortedDevices.Any())
            {
                Console.WriteLine("No available devices to move simulators to.");
                return;
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

                    // Remove the least loaded device from the set (this operation is O(log n))
                    sortedDevices.Remove(leastLoadedDevice);

                    // Update the device load and re-add it to the heap
                    float updatedLoad = GetDeviceLoad(leastLoadedDeviceName);
                    sortedDevices.Add((updatedLoad, leastLoadedDeviceName));  // Re-insert the device with updated load
                }
            }

            // Optionally, notify simulators of new assignments if needed
            NotifySimulatorsOfNewAssignments();
        }

        // Function to calculate the CPU and RAM usage combined
        public float GetDeviceLoad(string device)
        {
            var processes = Process.GetProcessesByName(device);  // Get processes by device name

            if (processes.Length > 0)
            {
                var process = processes[0];  // Get the first matching process (if multiple, take the first one)

                // Get the CPU and RAM usage for the process
                float cpuUsage = GetCpuUsageForDevice(process);  // Assume you have this method
                float ramUsage = GetRamUsageForDevice(process);  // Assume you have this method

                // Normalize CPU usage (0 to 1 scale)
                float normalizedCpuUsage = cpuUsage / 100.0f;  // Convert to 0-1 scale

                // Normalize RAM usage (0 to 1 scale based on total system RAM)
                float normalizedRamUsage = ramUsage / _totalSystemRam;  // Convert RAM usage to 0-1 scale

                // Combine the normalized CPU and RAM usage
                float combinedLoad = normalizedCpuUsage + normalizedRamUsage;

                return combinedLoad;  // Combined load between 0 and 2
            }

            return 0.0f;  // Return 0 if no process is found
        }

        private static float GetCpuUsageForDevice(Process process)
        {
            // Create the PerformanceCounter to get CPU usage of the specified process
            var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);

            // Call NextValue once to initialize the counter
            _ = cpuCounter.NextValue();

            // Wait for a brief moment (1 second) to get a valid reading
            Thread.Sleep(1000);

            // Return the CPU usage percentage
            return cpuCounter.NextValue();
        }


        private static float GetRamUsageForDevice(Process process)
        {
            // Get RAM usage for the process in bytes
            long memoryUsage = process.WorkingSet64;  // WorkingSet64 is the physical memory the process is using
            return (float)(memoryUsage / 1024.0 / 1024.0);  // Convert bytes to MB
        }

        private void RebalanceSimulators()
        {
            var devices = _registryManager.GetTelemetryDevices();
            var simulators = _registryManager.GetAllSimulators();

            foreach (var device in devices)
            {
                // Get simulators assigned to this device
                var assignedSimulators = _registryManager.GetSimulatorsAssignedToDevice(device);
                if (assignedSimulators.Count > 5) // Arbitrary threshold
                {
                    // Reassign simulators to less loaded devices (simplified logic)
                    foreach (var simulator in assignedSimulators)
                    {
                        // Pick the next available device
                        string newDevice = devices.FirstOrDefault(d => d != device);
                        if (!string.IsNullOrEmpty(newDevice))
                        {
                            _registryManager.RegisterSimulator(simulator, newDevice);
                            _simulatorReassignments[simulator] = newDevice;

                            Console.WriteLine($"[LoadMonitor] Reassigned {simulator} to {newDevice}");
                        }
                    }
                }
            }

            // Notify simulators of new assignments
            NotifySimulatorsOfNewAssignments();
        }

        private void NotifySimulatorsOfNewAssignments()
        {
            foreach (var simulator in _simulatorReassignments)
            {
                // Send a message to the simulator to connect to the new Telemetry Device
                string newDevice = simulator.Value;
                Console.WriteLine($"[LoadMonitor] Notifying simulator {simulator.Key} to connect to {newDevice}");

                // Implement actual logic to notify the simulator (e.g., using TCP or HTTP)
                // Here you would typically use a control channel to notify simulators of their new device address.
            }
        }
    }
}
