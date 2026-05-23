using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.GitHub;

namespace OpenClawNet.UnitTests.Storage;

public class SecretsVaultPhase1Tests
{
    private static ServiceProvider CreateServices(string? dbName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOpenClawStorage("Data Source=:memory:");
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        SchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        return sp;
    }

    [Fact]
    public async Task VaultFacade_ResolveAsync_ReturnsPlaintext_And_WritesAudit()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        await store.SetAsync("GitHub/Token", "vault-secret-value");

        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        var resolved = await vault.ResolveAsync("GitHub/Token", new VaultCallerContext(VaultCallerType.Tool, "unit", "s1"));

        Assert.Equal("vault-secret-value", resolved);
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.ToListAsync());
        Assert.Equal("GitHub/Token", audit.SecretName);
        Assert.Equal("Tool", audit.CallerType);
        Assert.Equal("unit", audit.CallerId);
        Assert.Equal("s1", audit.SessionId);
        Assert.True(audit.Success);
        Assert.Equal(SecretAccessAuditHashChain.GenesisHash, audit.PreviousRowHash);
        Assert.False(string.IsNullOrWhiteSpace(audit.RowHash));
    }

    [Fact]
    public async Task VaultFacade_MissingSecret_AuditsFailure_And_ThrowsVaultException()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();

        await Assert.ThrowsAsync<VaultException>(() => vault.ResolveAsync("Missing/Token", new VaultCallerContext(VaultCallerType.Tool, "unit")));

        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var audit = Assert.Single(await db.SecretAccessAudit.ToListAsync());
        Assert.Equal("Missing/Token", audit.SecretName);
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task VaultResolver_ResolvesVaultUri_And_UsesFiveMinuteTtlCache()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = new VaultConfigurationResolver(time, TimeSpan.FromMinutes(5));
        var vault = new Mock<IVault>(MockBehavior.Strict);
        vault.SetupSequence(v => v.ResolveAsync("Config/Secret", It.IsAny<VaultCallerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("first")
            .ReturnsAsync("second");

        Assert.True(VaultConfigurationResolver.TryParseVaultReference("vault://Config/Secret", out var name));
        Assert.Equal("Config/Secret", name);
        Assert.Equal("first", await resolver.ResolveSecretAsync(name, vault.Object));
        Assert.Equal("first", await resolver.ResolveSecretAsync(name, vault.Object));
        time.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        Assert.Equal("second", await resolver.ResolveSecretAsync(name, vault.Object));
        vault.Verify(v => v.ResolveAsync("Config/Secret", It.IsAny<VaultCallerContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task VaultResolver_InFlightResolveAfterRotation_ReturnsRotatedValueAndDoesNotCacheStaleValue()
    {
        var resolver = new VaultConfigurationResolver(new ManualTimeProvider(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        var releaseFirstResolve = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vault = new RotatingVault(releaseFirstResolve.Task) { CurrentValue = "old-value" };

        var inFlight = resolver.ResolveSecretAsync("Race/Secret", vault);
        await vault.FirstResolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        vault.CurrentValue = "rotated-value";
        resolver.Invalidate("Race/Secret");
        releaseFirstResolve.SetResult();

        Assert.Equal("rotated-value", await inFlight);
        Assert.Equal("rotated-value", await resolver.ResolveSecretAsync("Race/Secret", vault));
        Assert.Equal(2, vault.ResolveCount);
    }

    [Fact]
    public async Task Gate01_AuditRowWrittenForEveryVaultAccessAttempt_SuccessAndFailure()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretsStore>().SetAsync("A", "one");
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();

        await vault.ResolveAsync("A", new VaultCallerContext(VaultCallerType.System, "gate1"));
        await Assert.ThrowsAsync<VaultException>(() => vault.ResolveAsync("B", new VaultCallerContext(VaultCallerType.System, "gate1")));

        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var rows = await db.SecretAccessAudit.OrderBy(a => a.SecretName).ToListAsync();
        Assert.Collection(rows,
            r => Assert.True(r.Success),
            r => Assert.False(r.Success));
    }

    [Fact]
    public async Task Gate02_VaultValuesNeverCrossLlmContextBoundary_CapturedChatMessageIsRedacted()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretsStore>().SetAsync("Llm/Secret", "never-in-llm-payload");
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();
        await vault.ResolveAsync("Llm/Secret", new VaultCallerContext(VaultCallerType.Tool, "fake"));
        var sanitizer = new DefaultToolResultSanitizer(
            Microsoft.Extensions.Options.Options.Create(new ToolResultSanitizerOptions()),
            scope.ServiceProvider.GetRequiredService<IVaultSecretRedactor>());

        var captured = new ChatMessage
        {
            Role = ChatMessageRole.Tool,
            ToolCallId = "call-1",
            Content = sanitizer.Sanitize("tool accidentally returned never-in-llm-payload", "fake")
        };

        Assert.DoesNotContain("never-in-llm-payload", captured.Content, StringComparison.Ordinal);
        Assert.Contains("vault-secret-redacted", captured.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate03_GenericErrorMessageWhenVaultUnavailable_NoSecretNameLeak()
    {
        var vault = new Mock<IVault>();
        vault.Setup(v => v.ResolveAsync(GitHubTool.TokenSecretName, It.IsAny<VaultCallerContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VaultException());
        var services = new ServiceCollection()
            .AddSingleton(vault.Object)
            .AddSingleton<IVaultErrorShield, VaultErrorShield>()
            .BuildServiceProvider();
        var client = new Mock<Octokit.IGitHubClient>(MockBehavior.Strict);
        var tool = new GitHubTool(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<GitHubTool>.Instance, () => client.Object);

        var result = await tool.ExecuteAsync(new ToolInput
        {
            ToolName = "github",
            RawArguments = """{ "action": "summary", "owner": "elbruno", "repo": "openclawnet" }"""
        });

        Assert.False(result.Success);
        Assert.Equal(VaultErrorShield.GenericUnavailableMessage, result.Error);
        Assert.DoesNotContain(GitHubTool.TokenSecretName, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gate04_DataProtectionKeyRingPersistsAcrossProviderRestart_DecryptsExistingVaultCiphertext()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "TestResults", $"gate4-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "vault.db");
        var keyDir = Path.Combine(root, "dataprotection-keys");
        Directory.CreateDirectory(keyDir);

        try
        {
            await using (var first = CreatePersistentServices(dbPath, keyDir))
            {
                using var scope = first.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISecretsStore>()
                    .SetAsync("Persisted/Secret", "survives-provider-restart");
                await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
                var row = await db.Secrets.AsNoTracking().SingleAsync(s => s.Name == "Persisted/Secret");
                Assert.DoesNotContain("survives-provider-restart", row.EncryptedValue, StringComparison.Ordinal);
            }

            Assert.NotEmpty(Directory.EnumerateFiles(keyDir, "*.xml", SearchOption.AllDirectories));

            await using (var restarted = CreatePersistentServices(dbPath, keyDir))
            {
                using var scope = restarted.CreateScope();
                var value = await scope.ServiceProvider.GetRequiredService<ISecretsStore>()
                    .GetAsync("Persisted/Secret");
                Assert.Equal("survives-provider-restart", value);
            }
        }
        finally
        {
            await DeleteDirectoryBestEffortAsync(root);
        }
    }

    [Fact]
    public void Gate05_SecretAccessAuditData_NotReturnedByAnyOpenClawNetPublicSurface()
    {
        var violations = LoadOpenClawNetAssemblies()
            .SelectMany(GetLoadableTypes)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && ContainsSecretAccessAuditData(method.ReturnType))
                .Select(method => $"{type.FullName}.{method.Name} -> {method.ReturnType.FullName}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public async Task Gate06_SecretsEncryptedAtRest_CiphertextDoesNotContainPlaintext()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretsStore>().SetAsync("Encrypted", "plain-text-value");
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        var row = await db.Secrets.SingleAsync(s => s.Name == "Encrypted");
        Assert.DoesNotContain("plain-text-value", row.EncryptedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate07_DataProtectionPurposeStrings_AreCorrectAndImmutable()
    {
        var secretsPurpose = typeof(SecretsStore)
            .GetField("ProtectorPurpose", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue();
        var oauthPurpose = typeof(EncryptedSqliteOAuthTokenStore)
            .GetField("ProtectorPurpose", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue();
        Assert.Equal("OpenClawNet.Secrets.v1", secretsPurpose);
        Assert.Equal("OpenClawNet.OAuth.Google", oauthPurpose);
    }

    [Fact]
    public async Task Gate08_MigrationCliImportsUserSecret_AndVaultCanResolveIt()
    {
        await using var sp = CreateServices();
        var id = $"openclawnet-test-{Guid.NewGuid():N}";
        var projectDir = Path.Combine(Environment.CurrentDirectory, "TestResults", id);
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "ImportTest.csproj");
        await File.WriteAllTextAsync(projectPath, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UserSecretsId>{id}</UserSecretsId>
  </PropertyGroup>
</Project>
""");

        var secretsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id);
        Directory.CreateDirectory(secretsDir);
        var secretsFile = Path.Combine(secretsDir, "secrets.json");
        await File.WriteAllTextAsync(secretsFile, """{ "GoogleWorkspace:ClientSecret": "imported-secret-value" }""");

        try
        {
            var count = await SecretsImportCommand.ImportUserSecretsAsync(projectPath, sp);
            Assert.Equal(1, count);
            using var scope = sp.CreateScope();
            var resolved = await scope.ServiceProvider.GetRequiredService<IVault>()
                .ResolveAsync("GoogleWorkspace/ClientSecret", new VaultCallerContext(VaultCallerType.Cli, "test"));
            Assert.Equal("imported-secret-value", resolved);
        }
        finally
        {
            if (Directory.Exists(projectDir)) Directory.Delete(projectDir, recursive: true);
            if (Directory.Exists(secretsDir)) Directory.Delete(secretsDir, recursive: true);
        }
    }

    [Fact]
    public async Task Gate09_ConfigBindingVaultReference_ReturnsPlaintextAndWritesAuditRow()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretsStore>().SetAsync("Google/ClientSecret", "bound-secret");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleWorkspace:ClientSecret"] = "vault://Google/ClientSecret"
        });

        await configuration.AddResolvedVaultReferencesAsync(sp);
        var options = new GoogleWorkspaceOptionsProbe();
        configuration.GetSection("GoogleWorkspace").Bind(options);

        Assert.Equal("bound-secret", options.ClientSecret);
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        Assert.Contains(await db.SecretAccessAudit.ToListAsync(), a => a.SecretName == "Google/ClientSecret" && a.CallerType == "Configuration");
    }

    [Fact]
    public async Task Gate10_AuditHashChain_DetectsTamperedAuditRow()
    {
        await using var sp = CreateServices();
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretsStore>().SetAsync("Audit/Secret", "one");
        var vault = scope.ServiceProvider.GetRequiredService<IVault>();

        await vault.ResolveAsync("Audit/Secret", new VaultCallerContext(VaultCallerType.Tool, "unit", "s1"));
        await vault.ResolveAsync("Audit/Secret", new VaultCallerContext(VaultCallerType.Tool, "unit", "s2"));

        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync();
        Assert.True(await SecretAccessAuditHashChain.VerifyAsync(db));

        var first = await db.SecretAccessAudit.OrderBy(a => a.AccessedAt).ThenBy(a => a.Id).FirstAsync();
        first.CallerId = "tampered";
        await db.SaveChangesAsync();

        Assert.False(await SecretAccessAuditHashChain.VerifyAsync(db));
    }

    [Fact]
    public async Task Negative_ToolCallingVaultWithBadName_ReturnsGenericError_NoLeak()
    {
        await Gate03_GenericErrorMessageWhenVaultUnavailable_NoSecretNameLeak();
    }

    [Fact]
    public async Task Negative_VaultValueNeverInLlmPayload_FakeClientCapturesMessages()
    {
        const string secret = "captured-client-secret";
        var redactor = new VaultSecretRedactor();
        redactor.TrackResolvedValue(secret);
        var sanitizer = new DefaultToolResultSanitizer(
            Microsoft.Extensions.Options.Options.Create(new ToolResultSanitizerOptions()),
            redactor);
        var fake = new CapturingModelClient();
        await fake.CompleteAsync(new ChatRequest
        {
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatMessageRole.Tool,
                    ToolCallId = "tool-1",
                    Content = sanitizer.Sanitize($"secret={secret}", "fake")
                }
            ]
        });

        Assert.DoesNotContain(secret, string.Join("\n", fake.Captured.Select(m => m.Content)), StringComparison.Ordinal);
    }

    private static ServiceProvider CreatePersistentServices(string dbPath, string keyDir)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDir));
        services.AddOpenClawStorage($"Data Source={dbPath};Pooling=False");
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        SchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        return sp;
    }

    private static async Task DeleteDirectoryBestEffortAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static IReadOnlyList<Assembly> LoadOpenClawNetAssemblies()
    {
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "OpenClawNet.*.dll"))
        {
            try { Assembly.LoadFrom(file); }
            catch { }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("OpenClawNet.", StringComparison.Ordinal) == true)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
    }

    private static bool ContainsSecretAccessAuditData(Type? type)
    {
        if (type is null)
            return false;
        if (type == typeof(SecretAccessAuditEntity) || type.FullName?.Contains("SecretAccessAudit", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (type.HasElementType && ContainsSecretAccessAuditData(type.GetElementType()))
            return true;
        if (type.IsGenericType && type.GetGenericArguments().Any(ContainsSecretAccessAuditData))
            return true;
        return false;
    }

    private sealed class RotatingVault : IVault
    {
        private readonly Task _releaseFirstResolve;
        private int _resolveCount;

        public RotatingVault(Task releaseFirstResolve)
        {
            _releaseFirstResolve = releaseFirstResolve;
        }

        public TaskCompletionSource FirstResolveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? CurrentValue { get; set; }
        public int ResolveCount => Volatile.Read(ref _resolveCount);

        public async Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _resolveCount);
            if (call == 1)
            {
                FirstResolveStarted.SetResult();
                await _releaseFirstResolve.WaitAsync(ct);
            }

            return CurrentValue;
        }
    }

    private sealed class GoogleWorkspaceOptionsProbe
    {
        public string? ClientSecret { get; set; }
    }

    private sealed class CapturingModelClient : IModelClient
    {
        public List<ChatMessage> Captured { get; } = [];
        public string ProviderName => "fake";

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            Captured.AddRange(request.Messages);
            return Task.FromResult(new ChatResponse { Role = ChatMessageRole.Assistant, Content = "ok", Model = "fake" });
        }

        public async IAsyncEnumerable<ChatResponseChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Captured.AddRange(request.Messages);
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
