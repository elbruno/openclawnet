using System.Diagnostics;
using System.Globalization;
using OpenClawNet.PlaywrightDemoLauncher;
using Spectre.Console;

var repoRoot = FindRepoRoot();
if (args.Any(arg => arg is "-h" or "--help" or "help"))
{
    PrintHelp();
    return 0;
}

CleanupOrphanedPlaywrightNodeProcesses();

var includeAllPlaywright = args.Any(arg =>
    arg.Equals("--include-all-playwright", StringComparison.OrdinalIgnoreCase));

var catalog = PlaywrightDemoCatalog.Load(Path.Combine(repoRoot, "tests", "catalog.yaml"));

var playwrightTests = catalog.Tests
    .Where(test => test.Suite.Equals("playwright", StringComparison.OrdinalIgnoreCase))
    .Where(test => includeAllPlaywright || IsAttachedDemoTest(test))
    .Select(test => new LauncherTest(test))
    .ToList();

if (playwrightTests.Count == 0)
{
    if (includeAllPlaywright)
    {
        AnsiConsole.MarkupLine("[red]No Playwright tests were found in tests\\catalog.yaml.[/]");
    }
    else
    {
        AnsiConsole.MarkupLine(
            "[red]No attached demo tests were found. Add tests in tests\\OpenClawNet.PlaywrightTests\\Demos\\ with [Trait(\"Category\", \"DemoLive\")].[/]");
    }

    return 1;
}

var aspireStatus = await ResolveAspireStatusAsync(repoRoot);
if (!aspireStatus.IsRunning)
{
    RenderAspireNotRunning(aspireStatus);
    return 1;
}

RenderPrereqBanner(repoRoot, catalog, includeAllPlaywright, aspireStatus);

var categories = BuildCategories(playwrightTests);
var selectedCategory = AnsiConsole.Prompt(
    new SelectionPrompt<LauncherCategory>()
        .Title("Choose a [green]demo category[/]:")
        .PageSize(12)
        .MoreChoicesText("[grey](Use ↑/↓ to browse, Enter to select)[/]")
        .UseConverter(category => $"{category.Name} ({category.Tests.Count})")
        .AddChoices(categories));

var selectedTest = AnsiConsole.Prompt(
    new SelectionPrompt<LauncherTest>()
        .Title($"Choose a [green]{Markup.Escape(selectedCategory.Name)}[/] test:")
        .PageSize(14)
        .MoreChoicesText("[grey](Use ↑/↓ to browse, Enter to select)[/]")
        .UseConverter(test => $"{test.DisplayName} — {Truncate(test.Proves, 92)}")
        .AddChoices(selectedCategory.Tests));

var timingPreset = AnsiConsole.Prompt(
    new SelectionPrompt<TimingPreset>()
        .Title("Choose a [green]timing preset[/]:")
        .PageSize(10)
        .MoreChoicesText("[grey](Use ↑/↓ to browse, Enter to select)[/]")
        .UseConverter(preset => $"{preset.Name} ({preset.SlowMoMs} ms)")
        .AddChoices(TimingPresets.All));

RenderLaunchSummary(selectedCategory, selectedTest, timingPreset);

return await RunTestAsync(repoRoot, selectedTest, timingPreset);

static void RenderPrereqBanner(
    string repoRoot,
    PlaywrightDemoCatalog catalog,
    bool includeAllPlaywright,
    AspireStatus aspireStatus)
{
    var playwrightSuite = catalog.Suites.FirstOrDefault(s => s.Id.Equals("playwright", StringComparison.OrdinalIgnoreCase));
    var prereqText =
        "[yellow]Prerequisites[/]: Docker and Aspire are already running.\n" +
        "This launcher runs [bold]attached demo tests[/] and does [bold]not[/] start or stop Aspire.\n" +
        $"Catalog source: [grey]{Markup.Escape(Path.Combine("tests", "catalog.yaml"))}[/]\n" +
        $"Repo root: [grey]{Markup.Escape(repoRoot)}[/]\n" +
        $"Aspire web: [grey]{Markup.Escape(aspireStatus.WebUrl ?? "<not found>")}[/]\n" +
        $"Aspire gateway: [grey]{Markup.Escape(aspireStatus.GatewayUrl ?? "<not found>")}[/]";

    if (includeAllPlaywright)
    {
        prereqText += "\n[bold yellow]Mode[/]: include-all-playwright (non-demo tests visible)";
    }

    if (playwrightSuite is not null)
    {
        prereqText +=
            $"\nSuite: [grey]{Markup.Escape(playwrightSuite.Label)}[/] — {Markup.Escape(playwrightSuite.Description)}";
    }

    AnsiConsole.Write(new Panel(new Markup(prereqText))
        .Header("[bold]OpenClawNet Playwright Demo Launcher[/]")
        .Border(BoxBorder.Rounded)
        .Expand());
    Console.WriteLine();
}

static void PrintHelp()
{
    var helpText =
        "[bold]Usage[/]: OpenClawNet.PlaywrightDemoLauncher\n" +
        "[bold]Flow[/]: category → test → timing preset\n" +
        "[bold]Default scope[/]: attached demo tests only (`Demos/` + `Category=DemoLive`).\n" +
        "[bold]Optional[/]: use `--include-all-playwright` to show non-demo Playwright tests.\n" +
        "[bold]Runtime[/]: this launcher only runs `dotnet test`; it does not start or stop Aspire.\n" +
        $"[bold]Catalog[/]: {Markup.Escape(Path.Combine("tests", "catalog.yaml"))}";

    AnsiConsole.Write(new Panel(new Markup(helpText))
        .Header("[bold]Playwright demo launcher help[/]")
        .Border(BoxBorder.Rounded)
        .Expand());
}

static IReadOnlyList<LauncherCategory> BuildCategories(IEnumerable<LauncherTest> tests)
{
    var grouped = tests
        .GroupBy(GetCategoryName)
        .Select(group => new LauncherCategory(
            group.Key,
            group.OrderBy(test => test.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()))
        .OrderBy(category => CategoryRank(category.Name))
        .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return grouped;
}

static string GetCategoryName(LauncherTest test)
{
    var categories = new HashSet<string>(test.Category, StringComparer.OrdinalIgnoreCase);

    if (ContainsFolder(test.File, "Demos"))
    {
        return "Attached Aspire demos";
    }

    if (categories.Contains("ToolApproval"))
    {
        return "Tool approval";
    }

    if (categories.Contains("SecretsVault"))
    {
        return "Secrets vault";
    }

    if (categories.Contains("Skills"))
    {
        return "Skills";
    }

    if (categories.Contains("Chat"))
    {
        return "Chat";
    }

    if (categories.Contains("Sessions"))
    {
        return "Sessions";
    }

    if (categories.Contains("Gateway") || categories.Contains("Browser") || categories.Contains("Dashboard") || categories.Contains("Aspire"))
    {
        return "Platform & browser";
    }

    if (categories.Contains("Settings") || categories.Contains("Storage"))
    {
        return "Settings & storage";
    }

    if (categories.Contains("Jobs"))
    {
        return "Jobs";
    }

    if (categories.Contains("Channels"))
    {
        return "Channels";
    }

    if (categories.Contains("Agent") || categories.Contains("Provider"))
    {
        return "Agent & provider";
    }

    if (categories.Contains("WebsiteWatcher"))
    {
        return "Website watcher";
    }

    if (categories.Contains("Activity"))
    {
        return "Activity";
    }

    return "Other Playwright E2E";
}

static int CategoryRank(string name) => name switch
{
    "Attached Aspire demos" => 0,
    "Tool approval" => 1,
    "Chat" => 2,
    "Skills" => 3,
    "Secrets vault" => 4,
    "Platform & browser" => 5,
    "Settings & storage" => 6,
    "Sessions" => 7,
    "Jobs" => 8,
    "Channels" => 9,
    "Agent & provider" => 10,
    "Website watcher" => 11,
    "Activity" => 12,
    _ => 99
};

static bool IsAttachedDemoTest(PlaywrightDemoTest test)
{
    if (ContainsFolder(test.File, "Demos"))
    {
        return true;
    }

    return test.Category.Any(category => category.Equals("DemoLive", StringComparison.OrdinalIgnoreCase));
}

static bool ContainsFolder(string path, string folderName)
    => path.Contains($@"\{folderName}\", StringComparison.OrdinalIgnoreCase)
       || path.Contains($"/{folderName}/", StringComparison.OrdinalIgnoreCase);

static void RenderLaunchSummary(LauncherCategory category, LauncherTest test, TimingPreset preset)
{
    AnsiConsole.Write(new Panel(new Markup(
        $"[bold]Category:[/] {Markup.Escape(category.Name)}\n" +
        $"[bold]Test:[/] {Markup.Escape(test.DisplayName)}\n" +
        $"[bold]Pacing:[/] {Markup.Escape(preset.Name)} ({preset.SlowMoMs} ms)\n" +
        $"[bold]Filter:[/] {Markup.Escape(test.Filter)}\n" +
        $"[bold]Aspire:[/] already running"))
        .Header("[bold]Ready to launch[/]")
        .Border(BoxBorder.Double)
        .Expand());

    AnsiConsole.WriteLine();
}

static async Task<int> RunTestAsync(string repoRoot, LauncherTest test, TimingPreset preset)
{
    var launchStartedAtUtc = DateTime.UtcNow;
    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    startInfo.ArgumentList.Add("test");
    startInfo.ArgumentList.Add(Path.Combine("tests", "OpenClawNet.PlaywrightTests", "OpenClawNet.PlaywrightTests.csproj"));
    startInfo.ArgumentList.Add("--no-build");
    startInfo.ArgumentList.Add("--no-restore");
    startInfo.ArgumentList.Add("--filter");
    startInfo.ArgumentList.Add(test.Filter);
    startInfo.ArgumentList.Add("--logger");
    startInfo.ArgumentList.Add("console;verbosity=detailed");
    startInfo.ArgumentList.Add("--nologo");

    startInfo.Environment["PLAYWRIGHT_HEADED"] = "true";
    startInfo.Environment["PLAYWRIGHT_SLOWMO"] = preset.SlowMoMs.ToString(CultureInfo.InvariantCulture);

    using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    var noTestsMatched = false;

    ConsoleCancelEventHandler? handler = null;
    handler = (_, e) =>
    {
        e.Cancel = true;
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }
    };

    Console.CancelKeyPress += handler;
    try
    {
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            Console.WriteLine(eventArgs.Data);
            if (eventArgs.Data.Contains("No test matches the given testcase filter", StringComparison.OrdinalIgnoreCase))
            {
                noTestsMatched = true;
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            Console.Error.WriteLine(eventArgs.Data);
            if (eventArgs.Data.Contains("No test matches the given testcase filter", StringComparison.OrdinalIgnoreCase))
            {
                noTestsMatched = true;
            }
        };

        AnsiConsole.MarkupLine("[cyan]Launching test process...[/]");
        AnsiConsole.MarkupLine($"[grey]dotnet test {Markup.Escape(test.Filter)}[/]");

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet test.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (noTestsMatched)
        {
            AnsiConsole.MarkupLine("[red]Demo failed: selected test filter did not match any test case.[/]");
            return 2;
        }

        AnsiConsole.MarkupLine(process.ExitCode == 0
            ? "[green]Demo completed successfully.[/]"
            : $"[red]Demo finished with exit code {process.ExitCode}.[/]");
        return process.ExitCode;
    }
    finally
    {
        CleanupOrphanedPlaywrightNodeProcesses(launchStartedAtUtc);

        if (handler is not null)
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

static void CleanupOrphanedPlaywrightNodeProcesses(DateTime? launchStartedAtUtc = null)
{
    var cleaned = 0;
    foreach (var nodeProcess in Process.GetProcessesByName("node"))
    {
        try
        {
            if (launchStartedAtUtc.HasValue)
            {
                var startTimeUtc = nodeProcess.StartTime.ToUniversalTime();
                if (startTimeUtc < launchStartedAtUtc.Value.AddSeconds(-5))
                {
                    continue;
                }
            }

            var executablePath = nodeProcess.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                continue;
            }

            var normalizedPath = executablePath.Replace('/', '\\');
            if (!normalizedPath.Contains(@"\playwright-driver-cache\", StringComparison.OrdinalIgnoreCase) &&
                !normalizedPath.Contains(@"\.playwright\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            nodeProcess.Kill(entireProcessTree: true);
            cleaned++;
        }
        catch
        {
            // Ignore processes we cannot inspect or terminate.
        }
        finally
        {
            nodeProcess.Dispose();
        }
    }

    if (cleaned > 0)
    {
        AnsiConsole.MarkupLine($"[yellow]Cleaned up {cleaned} lingering Playwright node process(es).[/]");
    }
}

static async Task<AspireStatus> ResolveAspireStatusAsync(string repoRoot)
{
    var result = await RunCommandAsync("aspire", "describe --format Json", repoRoot, TimeSpan.FromSeconds(30));
    if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
    {
        return AspireStatus.NotRunning(result.Stderr);
    }

    var trimmed = result.Stdout.Trim();
    var jsonStart = trimmed.IndexOf('{');
    var jsonEnd = trimmed.LastIndexOf('}');
    if (jsonStart < 0 || jsonEnd <= jsonStart)
    {
        return AspireStatus.NotRunning("Aspire describe output did not contain JSON.");
    }

    try
    {
        using var json = System.Text.Json.JsonDocument.Parse(trimmed[jsonStart..(jsonEnd + 1)]);
        if (!json.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return AspireStatus.NotRunning("Aspire describe JSON did not contain resources.");
        }

        string? webUrl = null;
        string? gatewayUrl = null;
        foreach (var resource in resources.EnumerateArray())
        {
            if (!resource.TryGetProperty("displayName", out var nameProp) ||
                !resource.TryGetProperty("urls", out var urlsProp) ||
                urlsProp.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            var resourceName = nameProp.GetString() ?? string.Empty;
            var selectedUrl = urlsProp.EnumerateArray()
                .Select(urlNode => urlNode.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

            if (string.IsNullOrWhiteSpace(selectedUrl))
            {
                continue;
            }

            if (resourceName.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                webUrl = selectedUrl;
            }
            else if (resourceName.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                gatewayUrl = selectedUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(webUrl) || string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return AspireStatus.NotRunning("Aspire is running but web/gateway URLs were not resolved.");
        }

        return AspireStatus.Running(webUrl.TrimEnd('/'), gatewayUrl.TrimEnd('/'));
    }
    catch (Exception ex)
    {
        return AspireStatus.NotRunning($"Failed to parse `aspire describe` output: {ex.Message}");
    }
}

static void RenderAspireNotRunning(AspireStatus aspireStatus)
{
    var details = string.IsNullOrWhiteSpace(aspireStatus.Error)
        ? "Could not resolve Aspire resources."
        : aspireStatus.Error;

    AnsiConsole.Write(new Panel(new Markup(
            "[red]Aspire is not available for attached demo mode.[/]\n" +
            "Start Aspire first, then re-run the launcher.\n" +
            $"Details: [grey]{Markup.Escape(details)}[/]"))
        .Header("[bold]Attached demo prerequisites[/]")
        .Border(BoxBorder.Rounded)
        .Expand());
}

static async Task<CommandResult> RunCommandAsync(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
    {
        return new CommandResult(-1, string.Empty, $"Failed to start command: {fileName} {arguments}");
    }

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    var waitTask = process.WaitForExitAsync();
    var timeoutTask = Task.Delay(timeout);
    if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort timeout cleanup.
        }
    }

    await waitTask;
    return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "tests", "catalog.yaml")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static string Truncate(string value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
    {
        return value;
    }

    return value[..(maxLength - 1)] + "…";
}

internal sealed record LauncherCategory(string Name, IReadOnlyList<LauncherTest> Tests);

internal sealed record LauncherTest
{
    public LauncherTest(PlaywrightDemoTest test)
    {
        Test = test;
        Filter = BuildFilter(test);
    }

    public PlaywrightDemoTest Test { get; }

    public string DisplayName => Test.DisplayName;

    public string Proves => Test.Proves;

    public IReadOnlyList<string> Category => Test.Category;

    public string File => Test.File;

    public string Filter { get; }

    private static string BuildFilter(PlaywrightDemoTest test)
    {
        var namespacePrefix = BuildNamespacePrefix(test.File);
        var suffix = string.IsNullOrWhiteSpace(test.MethodName)
            ? test.ClassName
            : $"{test.ClassName}.{test.MethodName}";

        return $"FullyQualifiedName~OpenClawNet.PlaywrightTests.{namespacePrefix}{suffix}";
    }

    private static string BuildNamespacePrefix(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        const string marker = "tests/OpenClawNet.PlaywrightTests/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var relativePath = normalized[(markerIndex + marker.Length)..];
        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var segments = directory
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return string.Empty;
        }

        return string.Join('.', segments) + ".";
    }
}

internal sealed record TimingPreset(string Name, int SlowMoMs)
{
    public override string ToString() => Name;
}

internal static class TimingPresets
{
    public static readonly TimingPreset[] All =
    [
        new("Fast rehearsal", 800),
        new("Default narration", 1500),
        new("Slow narration", 2500),
        new("Full speed", 0)
    ];
}

internal sealed record AspireStatus(bool IsRunning, string? WebUrl, string? GatewayUrl, string? Error)
{
    public static AspireStatus Running(string webUrl, string gatewayUrl)
        => new(true, webUrl, gatewayUrl, null);

    public static AspireStatus NotRunning(string? error)
        => new(false, null, null, error);
}

internal sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
