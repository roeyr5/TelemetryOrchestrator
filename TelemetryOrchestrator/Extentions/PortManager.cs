using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Extentions
{
    public class PortManager
    {
        private int _currentTelemetryPort = 5000;
        private int _currentSimulatorPort = 4010;
        private readonly object _portLock = new();

        public (int TelemetryPort, int udpPort) GetNextPorts()
        {
            lock (_portLock)
            {
                _currentTelemetryPort++;
                _currentSimulatorPort += 5;
                var result = (_currentTelemetryPort, _currentSimulatorPort);
                return result;
            }
        }
    }
}
