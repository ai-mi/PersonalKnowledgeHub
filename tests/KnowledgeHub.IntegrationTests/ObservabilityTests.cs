using KnowledgeHub.IntegrationTests.Infrastructure;
using OpenTelemetry.Trace;

namespace KnowledgeHub.IntegrationTests;

public class ObservabilityTests(ApiFactory factory) : IntegrationTest(factory)
{
    // Адрес приёма указывает в никуда, чтобы тест не обращался к настоящему Application Insights.
    private const string FakeConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:1/";

    [Fact]
    public void WithoutConnectionString_AzureMonitorIsNotRegistered()
    {
        Factory.Services.GetService(typeof(TracerProvider)).ShouldBeNull();
    }

    [Fact]
    public async Task WithConnectionString_AzureMonitorIsRegistered_AndAppStillWorks()
    {
        using var monitored = Factory.WithWebHostBuilder(b =>
            b.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", FakeConnectionString));
        using var client = monitored.CreateClient();

        monitored.Services.GetService(typeof(TracerProvider)).ShouldNotBeNull();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task BlankConnectionString_IsTreatedAsDisabled()
    {
        using var blank = Factory.WithWebHostBuilder(b =>
            b.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "   "));
        using var client = blank.CreateClient();

        blank.Services.GetService(typeof(TracerProvider)).ShouldBeNull();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}