using Microsoft.AspNetCore.Mvc.Testing;

namespace TreeEditor.IntegrationTests;

/// <summary>
/// Boots the real API once per test collection against a dedicated Postgres database.
/// Redis is disabled through configuration so the in-process lock/cache fallbacks are used.
/// </summary>
public sealed class TreeEditorApiFixture : IDisposable
{
    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Client { get; }

    public TreeEditorApiFixture()
    {
        var connectionString = TestDatabase.Prepare();

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        Environment.SetEnvironmentVariable("Redis__Enabled", "false");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();
    }

    public async Task ResetAsync()
    {
        var response = await Client.PostAsync("/api/admin/reset", null);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Environment.SetEnvironmentVariable("Redis__Enabled", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<TreeEditorApiFixture>;
