// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Native schema initialization only; no credentials in arguments or output.
using System.Text.Json;
using MutSea.Data;
using MutSea.Data.PGSQL;
using Npgsql;
using System.Text.RegularExpressions;

if (args.Length != 1) throw new ArgumentException("Expected private database configuration path.");
var config = JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
var connection = new NpgsqlConnectionStringBuilder {
    Host = config.GetProperty("host").GetString(), Database = config.GetProperty("database").GetString(),
    Username = config.GetProperty("user").GetString(), Password = config.GetProperty("password").GetString(),
    Port = config.TryGetProperty("port", out var port) ? port.GetInt32() : 5432
};
using var db = new NpgsqlConnection(connection.ConnectionString);
try {
    db.Open();
    string[] stores = { "RegionStore", "EstateStore", "UserAccount", "Avatar", "AuthStore",
        "GridUserStore", "InventoryStore", "AssetStore", "FriendsStore", "UserProfiles", "Presence", "AgentPrefs" };
    foreach (string store in stores) {
        new Migration(db, typeof(PGSQLSimulationData).Assembly, store).Update();
        // The inherited runner can record a version after catching SQL errors.
        // A return from Update() alone is not schema-readiness evidence.
        Console.WriteLine("Native migration runner returned: " + store);
    }
    var assembly = typeof(PGSQLSimulationData).Assembly;
    foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".migrations"))) {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var versions = Regex.Matches(reader.ReadToEnd(), @"(?m)^:VERSION\s+(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        if (versions.Length == 0) continue;
        string store = resource.Split('.')[^2];
        if (!stores.Contains(store)) continue;
        using var version = new NpgsqlCommand("SELECT version FROM migrations WHERE name=@store", db);
        version.Parameters.AddWithValue("store", store);
        if (Convert.ToInt32(version.ExecuteScalar()) != versions.Max())
            throw new InvalidDataException("Native schema version mismatch: " + store);
    }
    foreach (string table in new[] { "prims", "primshapes", "terrain", "bakedterrain", "land",
        "regionsettings", "estate_settings", "useraccounts", "avatars", "auth", "tokens", "griduser",
        "inventoryitems", "inventoryfolders", "assets", "friends", "userprofile", "presence", "agentprefs" }) {
        using var check = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", db);
        check.Parameters.AddWithValue("table", "public." + table);
        if (!(bool)check.ExecuteScalar()!) throw new InvalidDataException("Missing native table: " + table);
    }
    foreach (var column in new[] { ("prims", "StartStr", "text"), ("agentprefs", "AccessPrefs", "character varying"),
        ("griduser", "Online", "character varying"), ("griduser", "LastPosition", "character varying") }) {
        using var check = new NpgsqlCommand("SELECT data_type FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column", db);
        check.Parameters.AddWithValue("table", column.Item1);
        check.Parameters.AddWithValue("column", column.Item2);
        if (check.ExecuteScalar()?.ToString() != column.Item3) throw new InvalidDataException("Native column contract mismatch: " + column.Item1 + "." + column.Item2);
    }
    Console.WriteLine("Native schema versions, required tables and repaired column contracts verified.");
    Console.WriteLine("Driver: " + typeof(NpgsqlConnection).Assembly.GetName().Version);
    return 0;
} catch (Exception error) {
    // Migration errors may contain SQL/connection information. Preserve privately.
    string path = args[0] + ".schema.diagnostic";
    File.WriteAllText(path, error.ToString());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.Error.WriteLine("Schema initialization failed: " + error.GetType().Name + "; private diagnostic saved.");
    return 1;
}
