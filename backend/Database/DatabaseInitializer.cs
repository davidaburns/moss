namespace Moss.Database;

using Npgsql;

public static class DatabaseInitializer {
    public static void EnsureDatabaseExists(string connectionString) {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;

        builder.Database = "postgres";

        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();

        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name;";
        checkCmd.Parameters.AddWithValue("name", databaseName);

        var exists = checkCmd.ExecuteScalar() != null;
        if (!exists) {
            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = $"CREATE DATABASE {databaseName};";
            createCmd.ExecuteNonQuery();
        }
    }
}
