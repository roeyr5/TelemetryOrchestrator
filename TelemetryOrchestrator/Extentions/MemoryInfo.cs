using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Extentions
{
    public static class MemoryInfo
    {
        public static float GetTotalSystemRam()
        {
            // Use WMI to get the total physical memory (RAM)
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");

            foreach (var queryObj in searcher.Get())
            {
                // The 'TotalPhysicalMemory' property is in bytes, so we divide by 1024 * 1024 to get MB
                ulong totalMemoryInBytes = (ulong)queryObj["TotalPhysicalMemory"];
                float totalMemoryInMB = totalMemoryInBytes / (1024 * 1024);  // Convert bytes to MB
                return totalMemoryInMB;
            }

            return 0.0f; // Return 0 if we can't fetch the total memory
        }
    }
}
