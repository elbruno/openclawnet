using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

// ---- Storage setup --------------------------------------------------------
string dbPath  = Path.Combine(AppContext.BaseDirectory, "profiles.db");
string connStr = $"Data Source={dbPath}";

using var db = new SqliteConnection(connStr);
db.Open();
InitSchema(db);
SeedProfiles(db);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

Console.WriteLine($"AgentProfileSwitcher — SQLite at {dbPath}");
Console.WriteLine("Type :help for commands, :quit to exit.");
Console.WriteLine();

// ---- REPL -----------------------------------------------------------------
while (true)
{
    string active = GetActive(db);
    Console.Write($"[{active}] > ");
    string? line = Console.ReadLine();
    if (line is null) { Console.WriteLine(); break; } // EOF / Ctrl-Z
    line = line.Trim();
    if (line.Length == 0) continue;

    if (line.StartsWith(':'))
    {
        if (HandleCommand(db, line)) break;
        continue;
    }

    var (instructions, model) = LoadActive(db);
    string effectiveModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? model;

    try
    {
        string reply = await ChatAsync(http, effectiveModel, instructions, line);
        Console.WriteLine($"[{active}] {reply}");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"❌ Ollama unreachable at http://localhost:11434 ({ex.Message}). Is `ollama serve` running?");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"❌ Chat failed: {ex.Message}");
    }
    Console.WriteLine();
}

return 0;

// ---- Commands -------------------------------------------------------------
bool HandleCommand(SqliteConnection db, string line)
{
    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string cmd = parts[0].ToLowerInvariant();
    string arg = parts.Length > 1 ? parts[1].Trim() : "";

    switch (cmd)
    {
        case ":quit":
        case ":exit":
            return true;

        case ":help":
            Console.WriteLine("  :list             list all profiles");
            Console.WriteLine("  :use <name>       switch active profile");
            Console.WriteLine("  :show             print active profile's full instructions");
            Console.WriteLine("  :add <name>       add a new profile (interactive)");
            Console.WriteLine("  :help             show this help");
            Console.WriteLine("  :quit | :exit     exit");
            Console.WriteLine("  <anything else>   send as a chat prompt to the active profile");
            break;

        case ":list":
            ListProfiles(db);
            break;

        case ":use":
            if (arg.Length == 0) { Console.WriteLine("usage: :use <name>"); break; }
            UseProfile(db, arg);
            break;

        case ":show":
            ShowActive(db);
            break;

        case ":add":
            if (arg.Length == 0) { Console.WriteLine("usage: :add <name>"); break; }
            AddProfile(db, arg);
            break;

        default:
            Console.WriteLine($"unknown command: {cmd} (try :help)");
            break;
    }
    return false;
}

// ---- DB helpers -----------------------------------------------------------
static void InitSchema(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS profiles (
          name TEXT PRIMARY KEY,
          instructions TEXT NOT NULL,
          model TEXT NOT NULL DEFAULT 'llama3.2'
        );
        CREATE TABLE IF NOT EXISTS state (
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );
        """;
    cmd.ExecuteNonQuery();
}

static void SeedProfiles(SqliteConnection db)
{
    const string codeReviewer =
        "You are a senior code reviewer. Read the user's snippet or question carefully. " +
        "Find bugs, race conditions, unclear naming, and missing edge cases. " +
        "Reply as a terse bulleted list. Be specific. " +
        "Do not write new features — only review what is shown.";

    const string pirate =
        "You are a friendly pirate first mate. Speak in pirate dialect (ahoy, ye, arr) " +
        "and use nautical metaphors (charts, rigging, fair winds). " +
        "Stay genuinely helpful — give correct, useful answers, just dressed in pirate voice.";

    Insert(db, "code-reviewer", codeReviewer, "llama3.2");
    Insert(db, "pirate",        pirate,        "llama3.2");

    using var seedActive = db.CreateCommand();
    seedActive.CommandText = "INSERT OR IGNORE INTO state(key, value) VALUES('active_profile', 'code-reviewer');";
    seedActive.ExecuteNonQuery();

    static void Insert(SqliteConnection db, string name, string instructions, string model)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO profiles(name, instructions, model) VALUES($n, $i, $m);";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$i", instructions);
        cmd.Parameters.AddWithValue("$m", model);
        cmd.ExecuteNonQuery();
    }
}

static string GetActive(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT value FROM state WHERE key = 'active_profile';";
    return (string?)cmd.ExecuteScalar() ?? "code-reviewer";
}

static (string Instructions, string Model) LoadActive(SqliteConnection db)
{
    string name = GetActive(db);
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT instructions, model FROM profiles WHERE name = $n;";
    cmd.Parameters.AddWithValue("$n", name);
    using var r = cmd.ExecuteReader();
    if (!r.Read()) return ("You are a helpful assistant.", "llama3.2");
    return (r.GetString(0), r.GetString(1));
}

static void ListProfiles(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT name, model, instructions FROM profiles ORDER BY name;";
    using var r = cmd.ExecuteReader();
    string active = GetActive(db);
    while (r.Read())
    {
        string n = r.GetString(0), m = r.GetString(1), inst = r.GetString(2);
        string preview = inst.Length <= 60 ? inst : inst.Substring(0, 60) + "…";
        string marker = n == active ? "*" : " ";
        Console.WriteLine($" {marker} {n,-16} ({m})  {preview}");
    }
}

static void UseProfile(SqliteConnection db, string name)
{
    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT 1 FROM profiles WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        if (check.ExecuteScalar() is null)
        {
            Console.WriteLine($"❌ no such profile: {name}");
            return;
        }
    }
    using var cmd = db.CreateCommand();
    // Single UPDATE — that's the whole "switch personas" operation.
    cmd.CommandText = "UPDATE state SET value = $n WHERE key = 'active_profile';";
    cmd.Parameters.AddWithValue("$n", name);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"✓ active profile: {name}");
}

static void ShowActive(SqliteConnection db)
{
    string name = GetActive(db);
    var (inst, model) = LoadActive(db);
    Console.WriteLine($"profile: {name}");
    Console.WriteLine($"model:   {model}");
    Console.WriteLine("instructions:");
    Console.WriteLine(inst);
}

static void AddProfile(SqliteConnection db, string name)
{
    using (var check = db.CreateCommand())
    {
        check.CommandText = "SELECT 1 FROM profiles WHERE name = $n;";
        check.Parameters.AddWithValue("$n", name);
        if (check.ExecuteScalar() is not null)
        {
            Console.WriteLine($"❌ profile already exists: {name}");
            return;
        }
    }

    Console.WriteLine("Enter instructions. End with a single '.' on its own line:");
    var lines = new List<string>();
    while (true)
    {
        string? l = Console.ReadLine();
        if (l is null || l == ".") break;
        lines.Add(l);
    }
    string instructions = string.Join('\n', lines).Trim();
    if (instructions.Length == 0) { Console.WriteLine("❌ no instructions provided; aborted"); return; }

    Console.Write("Model (blank for llama3.2): ");
    string model = (Console.ReadLine() ?? "").Trim();
    if (model.Length == 0) model = "llama3.2";

    using var cmd = db.CreateCommand();
    cmd.CommandText = "INSERT INTO profiles(name, instructions, model) VALUES($n, $i, $m);";
    cmd.Parameters.AddWithValue("$n", name);
    cmd.Parameters.AddWithValue("$i", instructions);
    cmd.Parameters.AddWithValue("$m", model);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"✓ added profile: {name}");
}

// ---- Ollama ---------------------------------------------------------------
static async Task<string> ChatAsync(HttpClient http, string model, string system, string user)
{
    var req = new ChatRequest(
        Model: model,
        Stream: false,
        Messages: new[]
        {
            new ChatMessage("system", system),
            new ChatMessage("user",   user),
        });

    using var resp = await http.PostAsJsonAsync("http://localhost:11434/api/chat", req);
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<ChatResponse>();
    return body?.Message?.Content?.Trim() ?? "(no response)";
}

// ---- DTOs -----------------------------------------------------------------
record ChatMessage(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

record ChatRequest(
    [property: JsonPropertyName("model")]    string Model,
    [property: JsonPropertyName("stream")]   bool Stream,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

record ChatResponse(
    [property: JsonPropertyName("message")] ChatMessage? Message);
