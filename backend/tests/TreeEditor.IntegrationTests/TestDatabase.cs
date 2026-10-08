using Npgsql;

namespace TreeEditor.IntegrationTests;

/// <summary>
/// Creates a throwaway database for the test run. The Docker/devenv Postgres exposes the
/// <c>postgres</c> maintenance database which we use to drop and recreate the target.
/// Override <c>TEST_POSTGRES_ADMIN</c> to point at another server.
/// </summary>
internal static class TestDatabase
{
    private const string DefaultAdminConnection =
        "Host=localhost;Port=5432;Database=postgres;Username=treeeditor;Password=treeeditor";

    private const string DefaultAppConnection =
        "Host=localhost;Port=5432;Database={0};Username=treeeditor;Password=treeeditor";

    public const string Name = "treeeditor_test";

    public static string ConnectionString =>
        string.Format(
            Environment.GetEnvironmentVariable("TEST_POSTGRES_APP") ?? DefaultAppConnection,
            Name);

    public static string Prepare()
    {
        var adminConnection =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN") ?? DefaultAdminConnection;

        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();

        Execute(connection, $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
        Execute(connection, $"CREATE DATABASE \"{Name}\"");

        return ConnectionString;
    }

    private static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
