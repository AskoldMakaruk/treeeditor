using System.Net.Http.Json;

namespace TreeEditor.IntegrationTests;

[Collection("api")]
public sealed class HealthApiTests(TreeEditorApiFixture fixture)
{
    [Fact]
    public async Task Health_reports_database_and_redis_status()
    {
        var response = await fixture.Client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.Equal("connected", body.Database);
        // Redis is disabled for the integration tests.
        Assert.Equal("disabled", body.Redis);
    }

    private sealed record HealthBody(string Status, string Database, string Redis);
}
