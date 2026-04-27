using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

// ---- Config ---------------------------------------------------------------
int windowSize = int.TryParse(Environment.GetEnvironmentVariable("MEMORY_WINDOW"), out var w) && w > 0 ? w : 6;
string model   = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";
string sessionId = Guid.NewGuid().ToString("N")[..8];

// ---- Storage --------------------------------------------------------------
string dbPath  = Path.Combine(AppContext.BaseDirectory, "memory.db");
string connStr = $"Data Source={dbPath}";
using var db = new SqliteConnection(connStr);
db.Open();
EnsureSchema(db);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

Console.WriteLine($"MemoryStub — SQLite at {dbPath}");
Console.WriteLine($"Session: {sessionId}  |  Memory window: last {windowSize} turns + 1 recall  |  Model: {model}");
Console.WriteLine("Type :help for commands, :quit to exit.");
Console.WriteLine();

// ---- REPL -----------------------------------------------------------------
while (true)
{
    Console.Write("> ");
    string? line = Console.ReadLine();
    if (line is null) { Console.WriteLine(); break; }
    line = line.Trim();
    if (line.Length == 0) continue;

    if (line.StartsWith(':'))
    {
        if (HandleCommand(db, line, sessionId)) break;
        continue;
    }

    PersistMessage(db, sessionId, "user", line);

    var recall = FindRecall(db, sessionId, line, windowSize);
    var recent = LoadRecent(db, sessionId, windowSize);

    var messages = new List<ChatMessage>
    {
        new("system",
            "You are a helpful assistant with two layers of memory: " +
            "(1) the last few turns of this conversation are included automatically, and " +
            "(2) one earlier snippet from prior conversation may be provided as a 'remembered' note. " +
            "Use the remembered snippet only if it is genuinely relevant to the current question; " +
            "otherwise ignore it."),
    };
    if (recall is not null)
    {
        string trimmed = recall.Value.Content.Length <= 200 ? recall.Value.Content : recall.Value.Content[..200] + "…";
        messages.Add(new ChatMessage("system",
            $"Remembered from session {recall.Value.SessionId} at {recall.Value.CreatedAt}: \"{trimmed}\""));
    }
    foreach (var m in recent) messages.Add(new ChatMessage(m.Role, m.Content));

    try
    {
        string reply = await CallOllama(http, model, messages);
        if (recall is not null)
            Console.WriteLine($"   [recalled from session {recall.Value.SessionId}]");
        Console.WriteLine($"assistant> {reply}");
        PersistMessage(db, sessionId, "assistant", reply);
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
bool HandleCommand(SqliteConnection db, string line, string sessionId)
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
            Console.WriteLine("  :history          last 20 messages of this session");
            Console.WriteLine("  :recall <text>    show what the recall layer would surface for <text>");
            Console.WriteLine("  :sessions         list distinct session ids with message counts");
            Console.WriteLine("  :forget           delete ALL stored messages (asks for confirm)");
            Console.WriteLine("  :help             show this help");
            Console.WriteLine("  :quit | :exit     exit");
            Console.WriteLine("  <anything else>   send as a chat prompt");
            break;

        case ":history":
            ShowHistory(db, sessionId);
            break;

        case ":recall":
            if (arg.Length == 0) { Console.WriteLine("usage: :recall <text>"); break; }
            var hit = FindRecall(db, sessionId, arg, int.MaxValue);
            if (hit is null) Console.WriteLine("(no recall above threshold)");
            else Console.WriteLine($"recall: session={hit.Value.SessionId} score={hit.Value.Score} at={hit.Value.CreatedAt}\n  {hit.Value.Content}");
            break;

        case ":sessions":
            ListSessions(db, sessionId);
            break;

        case ":forget":
            Console.Write("This deletes ALL messages across all sessions. Type YES to confirm: ");
            if ((Console.ReadLine() ?? "").Trim() == "YES")
            {
                using var del = db.CreateCommand();
                del.CommandText = "DELETE FROM messages;";
                int n = del.ExecuteNonQuery();
                Console.WriteLine($"✓ deleted {n} message(s)");
            }
            else Console.WriteLine("aborted");
            break;

        default:
            Console.WriteLine($"unknown command: {cmd} (try :help)");
            break;
    }
    return false;
}

static void ShowHistory(SqliteConnection db, string sessionId)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT id, role, content FROM messages WHERE session_id = $s ORDER BY id DESC LIMIT 20;";
    cmd.Parameters.AddWithValue("$s", sessionId);
    using var r = cmd.ExecuteReader();
    var rows = new List<(long id, string role, string content)>();
    while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
    rows.Reverse();
    if (rows.Count == 0) { Console.WriteLine("(no messages in this session yet)"); return; }
    foreach (var row in rows)
    {
        string preview = row.content.Length <= 80 ? row.content : row.content[..80] + "…";
        preview = preview.Replace('\n', ' ').Replace('\r', ' ');
        Console.WriteLine($"  #{row.id,-5} {row.role,-9} {preview}");
    }
}

static void ListSessions(SqliteConnection db, string current)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT session_id, COUNT(*), MIN(created_at) FROM messages GROUP BY session_id ORDER BY MIN(created_at);";
    using var r = cmd.ExecuteReader();
    bool any = false;
    while (r.Read())
    {
        any = true;
        string sid = r.GetString(0);
        long count = r.GetInt64(1);
        string first = r.GetString(2);
        string marker = sid == current ? "*" : " ";
        Console.WriteLine($" {marker} {sid}  msgs={count,-4}  first={first}");
    }
    if (!any) Console.WriteLine("(no sessions stored)");
}

// ---- DB helpers -----------------------------------------------------------
static void EnsureSchema(SqliteConnection db)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS messages (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          session_id TEXT NOT NULL,
          role TEXT NOT NULL,
          content TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_messages_session ON messages(session_id, id);
        """;
    cmd.ExecuteNonQuery();
}

static void PersistMessage(SqliteConnection db, string sessionId, string role, string content)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "INSERT INTO messages(session_id, role, content, created_at) VALUES($s, $r, $c, $t);";
    cmd.Parameters.AddWithValue("$s", sessionId);
    cmd.Parameters.AddWithValue("$r", role);
    cmd.Parameters.AddWithValue("$c", content);
    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
    cmd.ExecuteNonQuery();
}

static List<(string Role, string Content)> LoadRecent(SqliteConnection db, string sessionId, int n)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT role, content FROM messages WHERE session_id = $s ORDER BY id DESC LIMIT $n;";
    cmd.Parameters.AddWithValue("$s", sessionId);
    cmd.Parameters.AddWithValue("$n", n);
    using var r = cmd.ExecuteReader();
    var list = new List<(string, string)>();
    while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
    list.Reverse();
    return list;
}

static (string SessionId, string Content, string CreatedAt, int Score)? FindRecall(
    SqliteConnection db, string currentSession, string query, int windowSize)
{
    var queryTokens = Tokenize(query);
    if (queryTokens.Count == 0) return null;

    // Window start id for current session: id of the (windowSize+1)-th most recent message.
    long windowStartId = long.MaxValue;
    using (var w = db.CreateCommand())
    {
        w.CommandText = "SELECT id FROM messages WHERE session_id = $s ORDER BY id DESC LIMIT 1 OFFSET $o;";
        w.Parameters.AddWithValue("$s", currentSession);
        w.Parameters.AddWithValue("$o", windowSize);
        var o = w.ExecuteScalar();
        if (o is long l) windowStartId = l;
        else if (o is not null && long.TryParse(o.ToString(), out var lv)) windowStartId = lv;
    }

    using var cmd = db.CreateCommand();
    cmd.CommandText = """
        SELECT session_id, content, created_at
        FROM messages
        WHERE session_id != $s OR id < $w
        ORDER BY id DESC
        LIMIT 1000;
        """;
    cmd.Parameters.AddWithValue("$s", currentSession);
    cmd.Parameters.AddWithValue("$w", windowStartId);

    (string sid, string content, string ts, int score)? best = null;
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        string sid = r.GetString(0);
        string content = r.GetString(1);
        string ts = r.GetString(2);
        var tokens = Tokenize(content);
        int score = 0;
        foreach (var t in queryTokens) if (tokens.Contains(t)) score++;
        if (score >= 2 && (best is null || score > best.Value.score))
            best = (sid, content, ts, score);
    }
    return best;
}

static HashSet<string> Tokenize(string text)
{
    var seps = new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'', '-', '/', '\\' };
    return text.ToLowerInvariant()
        .Split(seps, StringSplitOptions.RemoveEmptyEntries)
        .Where(t => t.Length >= 4 && !Stopwords.Contains(t))
        .ToHashSet();
}

// ---- Ollama ---------------------------------------------------------------
static async Task<string> CallOllama(HttpClient http, string model, IReadOnlyList<ChatMessage> messages)
{
    var req = new ChatRequest(model, false, messages);
    using var resp = await http.PostAsJsonAsync("http://localhost:11434/api/chat", req);
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<ChatResponse>();
    return body?.Message?.Content?.Trim() ?? "(no response)";
}

// ---- Statics --------------------------------------------------------------
static class Stopwords
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "the", "and", "that", "this", "with", "from", "your", "have",
        "will", "what", "when", "where", "which", "would", "could", "should", "about"
    };
    public static bool Contains(string t) => Words.Contains(t);
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
