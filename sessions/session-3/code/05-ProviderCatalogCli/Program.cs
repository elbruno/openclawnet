using Microsoft.Data.Sqlite;

// ---- Storage setup --------------------------------------------------------
string dbPath = Environment.GetEnvironmentVariable("PROVIDERS_DB")
                ?? Path.Combine(AppContext.BaseDirectory, "providers.db");
string connStr = $"Data Source={dbPath}";

if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

string sub = args[0].ToLowerInvariant();

using var db = OpenConn(connStr);
EnsureSchema(db);

return sub switch
{
    "list"        => CmdList(db),
    "show"        => CmdShow(db, args),
    "add"         => CmdAdd(db, args),
    "update"      => CmdUpdate(db, args),
    "remove"      => CmdRemove(db, args),
    "set-default" => CmdSetDefault(db, args),
    "seed"        => CmdSeed(db),
    _ => UnknownSub(sub),
};

static int UnknownSub(string sub)
{
    Console.Error.WriteLine($"unknown subcommand: {sub}");
    PrintHelp();
    return 2;
}

// ---- Commands -------------------------------------------------------------
static int CmdList(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT name, kind, model, endpoint, is_default FROM providers ORDER BY name;";
    using var r = cmd.ExecuteReader();

    var rows = new List<(string n, string k, string m, string e, bool d)>();
    while (r.Read())
        rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) == 1));

    if (rows.Count == 0) { Console.WriteLine("(no providers — try `seed` or `add`)"); return 0; }

    int wN = Math.Max(4, rows.Max(x => x.n.Length));
    int wK = Math.Max(4, rows.Max(x => x.k.Length));
    int wM = Math.Max(5, rows.Max(x => x.m.Length));
    int wE = Math.Max(8, rows.Max(x => x.e.Length));

    Console.WriteLine($"{"NAME".PadRight(wN)}  {"KIND".PadRight(wK)}  {"MODEL".PadRight(wM)}  {"ENDPOINT".PadRight(wE)}  DEFAULT");
    Console.WriteLine($"{new string('-', wN)}  {new string('-', wK)}  {new string('-', wM)}  {new string('-', wE)}  -------");
    foreach (var x in rows)
        Console.WriteLine($"{x.n.PadRight(wN)}  {x.k.PadRight(wK)}  {x.m.PadRight(wM)}  {x.e.PadRight(wE)}  {(x.d ? "✓" : "")}");
    return 0;
}

static int CmdShow(SqliteConnection db, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("show: <name> required"); return 1; }
    string name = args[1];
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT name, kind, endpoint, model, api_key_env, is_default, created_at, updated_at FROM providers WHERE name = $n;";
    cmd.Parameters.AddWithValue("$n", name);
    using var r = cmd.ExecuteReader();
    if (!r.Read()) { Console.Error.WriteLine($"❌ no such provider: {name}"); return 1; }

    Console.WriteLine($"name:        {r.GetString(0)}");
    Console.WriteLine($"kind:        {r.GetString(1)}");
    Console.WriteLine($"endpoint:    {r.GetString(2)}");
    Console.WriteLine($"model:       {r.GetString(3)}");
    Console.WriteLine($"api_key_env: {(r.IsDBNull(4) ? "(none)" : r.GetString(4))}");
    Console.WriteLine($"is_default:  {(r.GetInt32(5) == 1 ? "yes" : "no")}");
    Console.WriteLine($"created_at:  {r.GetString(6)}");
    Console.WriteLine($"updated_at:  {r.GetString(7)}");
    return 0;
}

static int CmdAdd(SqliteConnection db, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("add: <name> required"); return 1; }
    string name = args[1];
    var flags = ParseFlags(args, 2);

    if (!flags.TryGetValue("kind", out var kind) ||
        !flags.TryGetValue("endpoint", out var endpoint) ||
        !flags.TryGetValue("model", out var model))
    {
        Console.Error.WriteLine("add: --kind, --endpoint, and --model are required");
        return 1;
    }
    flags.TryGetValue("key-env", out var keyEnv);

    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT 1 FROM providers WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        if (check.ExecuteScalar() is not null)
        {
            Console.Error.WriteLine($"❌ provider already exists: {name}");
            return 1;
        }
    }

    string now = DateTime.UtcNow.ToString("o");
    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        INSERT INTO providers(name, kind, endpoint, model, api_key_env, is_default, created_at, updated_at)
        VALUES($n, $k, $e, $m, $a, 0, $c, $u);
        """;
    cmd.Parameters.AddWithValue("$n", name);
    cmd.Parameters.AddWithValue("$k", kind);
    cmd.Parameters.AddWithValue("$e", endpoint);
    cmd.Parameters.AddWithValue("$m", model);
    cmd.Parameters.AddWithValue("$a", (object?)keyEnv ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$c", now);
    cmd.Parameters.AddWithValue("$u", now);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"✓ added provider: {name}");
    return 0;
}

static int CmdUpdate(SqliteConnection db, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("update: <name> required"); return 1; }
    string name = args[1];
    var flags = ParseFlags(args, 2);

    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT 1 FROM providers WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        if (check.ExecuteScalar() is null)
        {
            Console.Error.WriteLine($"❌ no such provider: {name}");
            return 1;
        }
    }

    if (flags.Count == 0) { Console.Error.WriteLine("update: at least one of --kind/--endpoint/--model/--key-env required"); return 1; }

    var sets = new List<string>();
    using var cmd = db.CreateCommand();
    if (flags.TryGetValue("kind",     out var k)) { sets.Add("kind = $k");        cmd.Parameters.AddWithValue("$k", k); }
    if (flags.TryGetValue("endpoint", out var e)) { sets.Add("endpoint = $e");    cmd.Parameters.AddWithValue("$e", e); }
    if (flags.TryGetValue("model",    out var m)) { sets.Add("model = $m");       cmd.Parameters.AddWithValue("$m", m); }
    if (flags.TryGetValue("key-env",  out var a)) { sets.Add("api_key_env = $a"); cmd.Parameters.AddWithValue("$a", (object?)a ?? DBNull.Value); }
    sets.Add("updated_at = $u");
    cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("o"));
    cmd.Parameters.AddWithValue("$n", name);
    cmd.CommandText = $"UPDATE providers SET {string.Join(", ", sets)} WHERE name = $n;";
    cmd.ExecuteNonQuery();
    Console.WriteLine($"✓ updated provider: {name}");
    return 0;
}

static int CmdRemove(SqliteConnection db, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("remove: <name> required"); return 1; }
    string name = args[1];

    bool wasDefault;
    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT is_default FROM providers WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        var v = check.ExecuteScalar();
        if (v is null) { Console.Error.WriteLine($"❌ no such provider: {name}"); return 1; }
        wasDefault = Convert.ToInt32(v) == 1;
    }

    using var del = db.CreateCommand();
    del.CommandText = "DELETE FROM providers WHERE name = $n;";
    del.Parameters.AddWithValue("$n", name);
    del.ExecuteNonQuery();
    Console.WriteLine($"✓ removed provider: {name}");
    if (wasDefault) Console.Error.WriteLine("⚠️  Default provider removed; no default set.");
    return 0;
}

static int CmdSetDefault(SqliteConnection db, string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("set-default: <name> required"); return 1; }
    string name = args[1];

    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT 1 FROM providers WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        if (check.ExecuteScalar() is null) { Console.Error.WriteLine($"❌ no such provider: {name}"); return 1; }
    }

    using var tx = db.BeginTransaction();
    using (var clear = db.CreateCommand())
    {
        clear.Transaction = tx;
        clear.CommandText = "UPDATE providers SET is_default = 0 WHERE is_default = 1;";
        clear.ExecuteNonQuery();
    }
    using (var set = db.CreateCommand())
    {
        set.Transaction = tx;
        set.CommandText = "UPDATE providers SET is_default = 1, updated_at = $u WHERE name = $n;";
        set.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("o"));
        set.Parameters.AddWithValue("$n", name);
        set.ExecuteNonQuery();
    }
    tx.Commit();
    Console.WriteLine($"✓ default provider: {name}");
    return 0;
}

static int CmdSeed(SqliteConnection db)
{
    using (var count = db.CreateCommand())
    {
        count.CommandText = "SELECT COUNT(*) FROM providers;";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0)
        {
            Console.WriteLine("(providers already present; seed skipped)");
            return 0;
        }
    }

    string now = DateTime.UtcNow.ToString("o");
    var seeds = new (string n, string k, string e, string m, string? a, int d)[]
    {
        ("local-llama",  "ollama",       "http://localhost:11434",                 "llama3.2",            null,                  1),
        ("openai-gpt4",  "openai",       "https://api.openai.com/v1",              "gpt-4o-mini",            "OPENAI_API_KEY",      0),
        ("azure-prod",   "azure-openai", "https://my-resource.openai.azure.com",   "gpt-4o",                 "AZURE_OPENAI_KEY",    0),
    };

    foreach (var s in seeds)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO providers(name, kind, endpoint, model, api_key_env, is_default, created_at, updated_at)
            VALUES($n, $k, $e, $m, $a, $d, $c, $u);
            """;
        cmd.Parameters.AddWithValue("$n", s.n);
        cmd.Parameters.AddWithValue("$k", s.k);
        cmd.Parameters.AddWithValue("$e", s.e);
        cmd.Parameters.AddWithValue("$m", s.m);
        cmd.Parameters.AddWithValue("$a", (object?)s.a ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", s.d);
        cmd.Parameters.AddWithValue("$c", now);
        cmd.Parameters.AddWithValue("$u", now);
        cmd.ExecuteNonQuery();
    }
    Console.WriteLine($"✓ seeded {seeds.Length} providers (default: local-llama)");
    return 0;
}

// ---- Helpers --------------------------------------------------------------
static SqliteConnection OpenConn(string connStr)
{
    var c = new SqliteConnection(connStr);
    c.Open();
    return c;
}

static void EnsureSchema(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS providers (
          name TEXT PRIMARY KEY,
          kind TEXT NOT NULL,
          endpoint TEXT NOT NULL,
          model TEXT NOT NULL,
          api_key_env TEXT,
          is_default INTEGER NOT NULL DEFAULT 0,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );
        """;
    cmd.ExecuteNonQuery();
}

static Dictionary<string, string?> ParseFlags(string[] args, int start)
{
    var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = start; i < args.Length; i++)
    {
        string a = args[i];
        if (!a.StartsWith("--")) continue;
        string key = a[2..];
        string? val = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : null;
        flags[key] = val;
    }
    return flags;
}

static void PrintHelp()
{
    Console.WriteLine("ProviderCatalogCli — manage an LLM provider catalog (SQLite).");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  list                                                    list all providers");
    Console.WriteLine("    e.g. dotnet run -- list");
    Console.WriteLine("  show <name>                                             print one provider");
    Console.WriteLine("    e.g. dotnet run -- show local-llama");
    Console.WriteLine("  add <name> --kind <k> --endpoint <u> --model <m> [--key-env <V>]");
    Console.WriteLine("    e.g. dotnet run -- add my-claude --kind anthropic --endpoint https://api.anthropic.com --model claude-3-5-sonnet --key-env ANTHROPIC_API_KEY");
    Console.WriteLine("  update <name> [--kind <k>] [--endpoint <u>] [--model <m>] [--key-env <V>]");
    Console.WriteLine("    e.g. dotnet run -- update local-llama --model llama3.2");
    Console.WriteLine("  remove <name>                                           delete one provider");
    Console.WriteLine("    e.g. dotnet run -- remove my-claude");
    Console.WriteLine("  set-default <name>                                      mark provider as the default");
    Console.WriteLine("    e.g. dotnet run -- set-default openai-gpt4");
    Console.WriteLine("  seed                                                    insert 3 sample providers if empty");
    Console.WriteLine("    e.g. dotnet run -- seed");
    Console.WriteLine("  help | -h | --help                                      show this help");
    Console.WriteLine();
    Console.WriteLine("DB path: ./providers.db (override with PROVIDERS_DB env var).");
}
