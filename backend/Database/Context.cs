namespace Moss.Database;

using System.Data;
using Npgsql;

public interface IDbContext {
    IDbConnection CreateConnection();
}

public class DapperContext: IDbContext {
    private readonly string _connectionString;

    public DapperContext(IConfiguration config) {
        _connectionString = config.GetConnectionString("Database");
    }

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
