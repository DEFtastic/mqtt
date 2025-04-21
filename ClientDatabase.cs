using Microsoft.Data.Sqlite;

namespace MeshtasticMqtt;

public class ClientDatabase
{
    private const string DatabaseFile = "clients.db";

    public void InitializeDatabase()
    {
        if (File.Exists(DatabaseFile))
            return;

        using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
        @"
            CREATE TABLE Clients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClientId TEXT NOT NULL,
                ConnectedAt TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void InsertClient(string clientId)
    {
        using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
        @"
            INSERT INTO Clients (ClientId, ConnectedAt)
            VALUES ($clientId, $connectedAt);
        ";
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.Parameters.AddWithValue("$connectedAt", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}