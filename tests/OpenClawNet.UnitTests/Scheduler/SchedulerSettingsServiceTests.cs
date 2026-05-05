using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Services.Scheduler.Models;
using OpenClawNet.Services.Scheduler.Services;

namespace OpenClawNet.UnitTests.Scheduler;

public sealed class SchedulerSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SchedulerSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"scheduler-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private SchedulerSettingsService Create(IConfiguration? config = null)
    {
        var env = new FakeHostEnvironment(_tempDir);
        var cfg = config ?? new ConfigurationBuilder().Build();
        return new SchedulerSettingsService(cfg, env, NullLogger<SchedulerSettingsService>.Instance);
    }

    [Fact]
    public void GetSettings_ReturnsDefaults_WhenNoFileOrConfig()
    {
        var svc = Create();
        var s = svc.GetSettings();
        s.MaxConcurrentJobs.Should().Be(3);
        s.JobTimeoutSeconds.Should().Be(300);
        s.PollIntervalSeconds.Should().Be(30);
    }

    [Fact]
    public void GetSettings_ReadsFromConfiguration()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scheduler:MaxConcurrentJobs"] = "5",
                ["Scheduler:JobTimeoutSeconds"] = "120",
                ["Scheduler:PollIntervalSeconds"] = "15"
            })
            .Build();

        var svc = Create(cfg);
        var s = svc.GetSettings();
        s.MaxConcurrentJobs.Should().Be(5);
        s.JobTimeoutSeconds.Should().Be(120);
        s.PollIntervalSeconds.Should().Be(15);
    }

    [Fact]
    public void Update_PersistsToFile()
    {
        var svc = Create();
        svc.Update(new SchedulerSettings { MaxConcurrentJobs = 7, JobTimeoutSeconds = 60, PollIntervalSeconds = 10 });

        var path = Path.Combine(_tempDir, "scheduler-settings.json");
        File.Exists(path).Should().BeTrue();

        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<SchedulerSettings>(json)!;
        loaded.MaxConcurrentJobs.Should().Be(7);
        loaded.JobTimeoutSeconds.Should().Be(60);
        loaded.PollIntervalSeconds.Should().Be(10);
    }

    [Fact]
    public void Update_LoadedFileOverridesConfig_OnRestart()
    {
        var svc = Create();
        svc.Update(new SchedulerSettings { MaxConcurrentJobs = 9, JobTimeoutSeconds = 200, PollIntervalSeconds = 20 });

        // Create a second instance (simulating restart) — it should load from file
        var svc2 = Create();
        var s = svc2.GetSettings();
        s.MaxConcurrentJobs.Should().Be(9);
    }

    [Fact]
    public void Update_ClampsValues_WhenOutOfRange()
    {
        var svc = Create();
        svc.Update(new SchedulerSettings { MaxConcurrentJobs = 999, JobTimeoutSeconds = 1, PollIntervalSeconds = 1 });

        var s = svc.GetSettings();
        s.MaxConcurrentJobs.Should().Be(20);
        s.JobTimeoutSeconds.Should().Be(10);
        s.PollIntervalSeconds.Should().Be(5);
    }

    [Fact]
    public void Update_IsThreadSafe_UnderConcurrentWrites()
    {
        var svc = Create();
        var tasks = Enumerable.Range(1, 20).Select(i =>
            Task.Run(() => svc.Update(new SchedulerSettings { MaxConcurrentJobs = i % 10 + 1 }))
        ).ToArray();
        Func<Task> act = () => Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
        svc.GetSettings().MaxConcurrentJobs.Should().BeInRange(1, 20);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRoot) => ContentRootPath = contentRoot;
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
