using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TelemetryOrchestrator.Extentions;
using System.Text.Json;

namespace TelemetryOrchestrator.Services.Http_Requests
{
    public class HttpService
    {
        private readonly HttpClient _httpClient;
        private const int simulatorPort = 7000;

        public static object JsonConvert { get; private set; }

        public HttpService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<OperationResult> StartTelemetryPipeline(int devicePort , int udpPort, int uavNumber)
        {
            ChannelDTO channelDto = new ChannelDTO
            {
                uavNumber = uavNumber,
                port = udpPort
            };

            string serializedData = JsonSerializer.Serialize(channelDto);
            StringContent content = new StringContent(serializedData, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"http://localhost:{devicePort}/Start", content);

            return response.IsSuccessStatusCode
                ? OperationResult.Success
                : OperationResult.Failed;
        }

        public async Task<OperationResult> ConfigureSimulator(int uavNumber, int udpPort)
        {
            ChannelDTO channelDto = new ChannelDTO
            {
                uavNumber = uavNumber,
                port = udpPort
            };
            
            var serializedData = JsonSerializer.Serialize(channelDto);
            var content = new StringContent(serializedData, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"http://localhost:{simulatorPort}/configure", content);

            return response.IsSuccessStatusCode
                ? OperationResult.Success
                : OperationResult.Failed;
        }
    }

}
