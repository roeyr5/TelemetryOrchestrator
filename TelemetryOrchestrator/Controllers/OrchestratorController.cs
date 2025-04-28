using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;
using TelemetryOrchestrator.Extentions;
using TelemetryOrchestrator.Interfaces;
using TelemetryOrchestrator.Services;
using TelemetryOrchestrator.Services.Http_Requests;

namespace TelemetryOrchestrator.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrchestratorController : ControllerBase
    {
        private readonly IRegistryManager _registryManager;
        private readonly LoadMonitorService _loadMonitor;
        private readonly HttpService _httpManager;

        public OrchestratorController(IRegistryManager registryManagerService, LoadMonitorService loadMonitorService, HttpService httpService)
        {
            _registryManager = registryManagerService;
            _loadMonitor = loadMonitorService;
            _httpManager = httpService;
        }

        [HttpPost("newUav")]
        public async Task<IActionResult> NewUav([FromBody] ChannelDTO request)
        {
            var (devicePort, listeningPort , deviceId) = _loadMonitor.GetMinLoadedPorts();

            OperationResult telemetryResult = await _httpManager.StartTelemetryPipeline(devicePort, listeningPort, request.uavNumber);
            if (telemetryResult != OperationResult.Success) return BadRequest("Telemetry create Pipeline failed");


            OperationResult simulatorResult = await _httpManager.ConfigureSimulator(request.uavNumber, listeningPort);
            if (simulatorResult != OperationResult.Success) return BadRequest("simulator failed");

            _registryManager.RegisterSimulator(new SimulatorInfo(request.uavNumber,listeningPort),deviceId);

            return Ok();


        }

    }
}
