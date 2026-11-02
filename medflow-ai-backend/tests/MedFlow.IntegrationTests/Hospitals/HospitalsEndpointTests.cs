using System.Net;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Hospitals;
using MedFlow.IntegrationTests.Auth;

namespace MedFlow.IntegrationTests.Hospitals;

public sealed class HospitalsEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public HospitalsEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListHospitals_Anonymous_Returns200WithSeededDemoHospital()
    {
        var response = await _client.GetAsync("/api/v1/hospitals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hospitals = await response.Content.ReadFromJsonAsync<List<HospitalDto>>();
        Assert.NotNull(hospitals);
        Assert.NotEmpty(hospitals);

        var demoHospital = Assert.Single(hospitals, h => h.HospitalCode == "HOS-DEMO-01");
        Assert.Equal("Bệnh viện MedFlow (giả lập)", demoHospital.Name);
        Assert.True(demoHospital.IsActive);
    }
}
