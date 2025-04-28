using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Extentions
{
    public static class MemoryInfo
    {

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static float GetTotalPhysicalMemory()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return (float)(memStatus.ullTotalPhys / 1024 / 1024);
            }
            else
            {
                throw new InvalidOperationException("Cannot get total physical memory.");
            }

        }

        public static async Task<float> GetDeviceLoad(int deviceId , float totalSystemRam)
        {
            Process process = Process.GetProcessById(deviceId);

            float cpuUsage = await GetCpuUsageForDeviceAsync(process);
            float ramUsage = GetRamUsageForDevice(process);

            // Normalize CPU usage (0 to 1 scale)
            float normalizedCpuUsage = cpuUsage / 100.0f;  // Convert to 0-1 scale

            // Normalize RAM usage (0 to 1 scale based on total system RAM)
            float normalizedRamUsage = ramUsage / totalSystemRam;  // Convert RAM usage to 0-1 scale

            // Combine the normalized CPU and RAM usage
            float combinedLoad = normalizedCpuUsage + normalizedRamUsage;

            return combinedLoad;  // Combined load between 0 and 2

            //return 0.0f; 
        }

        public static async Task<float> GetCpuUsageForDeviceAsync(Process process)
        {

            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            await Task.Delay(1000);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            float cpuUsageTotal = (float)(cpuUsedMs / (Environment.ProcessorCount * totalMsPassed));

            return cpuUsageTotal;
        }


        public static float GetRamUsageForDevice(Process process)
        {
            long memoryUsage = process.WorkingSet64;  
            return (float)(memoryUsage / 1024.0 / 1024.0); 
        }


    }
}
