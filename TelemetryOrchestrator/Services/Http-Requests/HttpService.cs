using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TelemetryOrchestrator.Extentions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System;

namespace TelemetryOrchestrator.Services.Http_Requests
{
    public class HttpService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly int _simulatorPort;

        public static object JsonConvert { get; private set; }

        public HttpService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _baseUrl = configuration.GetValue<string>("ApiSettings:BaseUrl");
            _simulatorPort = configuration.GetValue<int>("ApiSettings:SimulatorPort");

        }

        public async Task<OperationResult> StartTelemetryPipeline(int devicePort, int udpPort, int uavNumber)
        {
            try
            {

                ChannelDTO channelDto = new()
                {
                    uavNumber = uavNumber,
                    port = udpPort
                };

                string serializedData = JsonSerializer.Serialize(channelDto);
                StringContent content = new(serializedData, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}:{devicePort}/Start", content);

                return response.IsSuccessStatusCode
                    ? OperationResult.Success
                    : OperationResult.Failed;
            }
            catch(Exception e)
            {
                System.Console.WriteLine("error" +e);
                return OperationResult.Failed;
            }
        }

        public async Task<OperationResult> ConfigureSimulator(int uavNumber, int udpPort)
        {
            ChannelDTO channelDto = new()
            {
                uavNumber = uavNumber,
                port = udpPort
            };

            string serializedData = JsonSerializer.Serialize(channelDto);
            StringContent content = new(serializedData, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}:{_simulatorPort}/simulator/StartIcd", content);

            return response.IsSuccessStatusCode
                ? OperationResult.Success
                : OperationResult.Failed;
        }

        public async Task<OperationResult> ReconfigureSimulatorEndpoint(int uavNumber, int listenPort, int devicePort)
        {
            ChannelDTO changeEndPointDto = new()
            {
                uavNumber = uavNumber,
                port = listenPort
            };

            string serializedData = JsonSerializer.Serialize(changeEndPointDto);
            StringContent content = new(serializedData, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PutAsync($"{_baseUrl}:{_simulatorPort}/simulator/ChangeEndPoints", content);
            await StartTelemetryPipeline(devicePort, listenPort, uavNumber);

            return response.IsSuccessStatusCode
                            ? OperationResult.Success
                            : OperationResult.Failed;
        }


    }

}
