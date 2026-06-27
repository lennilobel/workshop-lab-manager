using Azure;
using Azure.Core;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Helpers
{
	public class SqlDatabaseHelper
	{
		public static async Task<SqlDatabaseResource> CreateDatabase(SqlServerResource server, string databaseName, CancellationToken cancellationToken)
		{
			var databases = server.GetSqlDatabases();
			var databaseData = new SqlDatabaseData(new AzureLocation(Program.Context.AppConfig.TargetRegionName))
			{
				Sku = new SqlSku(Program.Context.AppConfig.SqlDatabase.DatabaseSku),
				Collation = "Latin1_General_100_CI_AS_SC_UTF8",
			};
			var createOperation = await databases.CreateOrUpdateAsync(WaitUntil.Completed, databaseName, databaseData, cancellationToken);
			var database = createOperation.Value;

			return database;
		}

		public static async Task ExecuteSql(string serverName, string databaseName, string username, string password, string sql, CancellationToken cancellationToken)
		{
			var connectionString =
				$"Server=tcp:{serverName}.database.windows.net,1433;" +
				$"Database={databaseName};" +
				$"User ID={username};Password={password};" +
				"Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

			using var sqlConnection = new SqlConnection(connectionString);
			await sqlConnection.OpenAsync(cancellationToken);
			using var cmd = sqlConnection.CreateCommand();
			cmd.CommandTimeout = 600; // 10 minutes
			cmd.CommandText = sql;

			await cmd.ExecuteNonQueryAsync(cancellationToken);
		}

	}
}
