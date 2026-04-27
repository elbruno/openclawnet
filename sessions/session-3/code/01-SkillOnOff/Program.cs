using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ---- Inputs ---------------------------------------------------------------
string prompt    = args.Length > 0 ? args[0] : "Write a short bio for a senior developer.";
string skillName = args.Length > 1 ? args[1] : "concise-tone";
string model     = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";

string skillPath = Path.Combine(AppContext.BaseDirectory, "skills", $"{skillName}.skill.md");
if (!File.Exists(skillPath))
{
    // Fall back to source-tree skills folder for `dotnet run` (bin/ may not have it copied yet).
    var srcSkill = Path.Combine(Directory.GetCurrentDirectory(), "skills", $"{skillName}.skill.md");
    if (File.Exists(srcSkill)) skillPath = srcSkill;
}

if (!File.Exists(skillPath))
{
    Console.Error.WriteLine($"❌ Skill not found: skills/{skillName}.skill.md");
    return 1;
}

string skillBody = LoadSkillBody(skillPath);

// ---- Two calls in parallel -----------------------------------------------
string baseSystem = "You are a helpful assistant.";
string skillSystem = baseSystem + "\n\n" + skillBody;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

Console.WriteLine($"Model:  {model}");
Console.WriteLine($"Skill:  {skillName}");
Console.WriteLine($"Prompt: {prompt}");
Console.WriteLine();
Console.WriteLine("Calling Ollama twice (raw + skill)...");
Console.WriteLine();

string rawText, skillText;
try
{
    var rawTask   = ChatAsync(http, model, baseSystem,  prompt);
    var skillTask = ChatAsync(http, model, skillSystem, prompt);
    await Task.WhenAll(rawTask, skillTask);
    rawText   = rawTask.Result;
    skillText = skillTask.Result;
}
catch (HttpRequestException)
{
    Console.Error.WriteLine("❌ Could not reach Ollama at http://localhost:11434. Is `ollama serve` running?");
    return 1;
}

// ---- Side-by-side render --------------------------------------------------
const int col = 50;
string leftHeader  = " RAW ".PadRight(col, '─');
string rightHeader = $" WITH skill: {skillName} ".PadRight(col, '─');
Console.WriteLine($"─── {leftHeader}  ─── {rightHeader}");

var leftLines  = Wrap(rawText,   col).ToList();
var rightLines = Wrap(skillText, col).ToList();
int rows = Math.Max(leftLines.Count, rightLines.Count);
for (int i = 0; i < rows; i++)
{
    string l = i < leftLines.Count  ? leftLines[i]  : "";
    string r = i < rightLines.Count ? rightLines[i] : "";
    Console.WriteLine($"{l.PadRight(col + 4)}  {r}");
}

return 0;

// ---- Helpers --------------------------------------------------------------

static string LoadSkillBody(string path)
{
    var lines = File.ReadAllLines(path);
    if (lines.Length == 0 || lines[0].Trim() != "---")
        return string.Join('\n', lines).Trim();

    int end = -1;
    for (int i = 1; i < lines.Length; i++)
    {
        if (lines[i].Trim() == "---") { end = i; break; }
    }
    if (end < 0) return string.Join('\n', lines).Trim();
    return string.Join('\n', lines.Skip(end + 1)).Trim();
}

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

static IEnumerable<string> Wrap(string text, int width)
{
    foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
    {
        if (paragraph.Length == 0) { yield return ""; continue; }

        var sb = new StringBuilder();
        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length == 0) { sb.Append(word); continue; }
            if (sb.Length + 1 + word.Length > width)
            {
                yield return sb.ToString();
                sb.Clear();
                sb.Append(word);
            }
            else
            {
                sb.Append(' ').Append(word);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
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
