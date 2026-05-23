using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.CLI;

[Trait("Category", "CLI")]
[Trait("Phase", "5")]
public sealed class VaultCommandTests
{
    [Fact]
    public async Task List_WithEmptyVault_ExitsCleanly()
    {
        using var fixture = await VaultCliFixture.CreateAsync();

        var result = await fixture.RunCliAsync("list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("(no secrets)", result.StdOut);
    }

    [Fact]
    public async Task ListAndListVersions_ShowMetadataOnly()
    {
        using var fixture = await VaultCliFixture.CreateAsync();
        await fixture.Store.SetAsync("CLIMetadata", "secret-value", "CLI metadata test");

        var list = await fixture.RunCliAsync("list");
        var versions = await fixture.RunCliAsync("list-versions", "CLIMetadata");

        Assert.Equal(0, list.ExitCode);
        Assert.Contains("CLIMetadata", list.StdOut);
        Assert.Contains("CLI metadata test", list.StdOut);
        Assert.DoesNotContain("secret-value", list.StdOut);
        Assert.Equal(0, versions.ExitCode);
        Assert.Contains("1", versions.StdOut);
    }

    [Fact]
    public async Task Rotate_ReadsValueFromStdinAndCreatesNewVersion()
    {
        using var fixture = await VaultCliFixture.CreateAsync();
        await fixture.Store.SetAsync("CLIRotate", "v1");

        var result = await fixture.RunCliAsync(["rotate", "CLIRotate"], stdin: "v2");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("v2", await fixture.Store.GetAsync("CLIRotate"));
        Assert.Equal([1, 2], await fixture.Store.ListVersionsAsync("CLIRotate"));
        Assert.DoesNotContain("v2", result.StdOut);
    }

    [Fact]
    public async Task Purge_RequiresForceAndPreservesSecretWithoutIt()
    {
        using var fixture = await VaultCliFixture.CreateAsync();
        await fixture.Store.SetAsync("CLIPurge", "demo-value");
        Assert.True(await fixture.Store.DeleteAsync("CLIPurge"));

        var rejected = await fixture.RunCliAsync("purge", "CLIPurge");
        var versionsAfterRejectedPurge = await fixture.Store.ListVersionsAsync("CLIPurge");

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("--force", rejected.StdErr);
        Assert.Equal([1], versionsAfterRejectedPurge);
    }

    [Fact]
    public async Task AuditVerify_CleanChain_ExitsZero()
    {
        using var fixture = await VaultCliFixture.CreateAsync();
        await fixture.Store.SetAsync("CLIAudit", "demo-value");

        var result = await fixture.RunCliAsync("audit-verify");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("valid", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class VaultCliFixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _root;

        private VaultCliFixture(ServiceProvider provider, string root, string dbPath, string keysPath)
        {
            _provider = provider;
            _root = root;
            DbPath = dbPath;
            KeysPath = keysPath;
            Store = provider.GetRequiredService<ISecretsStore>();
        }

        public string DbPath { get; }
        public string KeysPath { get; }
        public ISecretsStore Store { get; }

        public static async Task<VaultCliFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "openclawnet-vault-cli-tests", Guid.NewGuid().ToString("N"));
            var keysPath = Path.Combine(root, "keys");
            var dbPath = Path.Combine(root, "vault.db");
            Directory.CreateDirectory(keysPath);

            var services = new ServiceCollection();
            services.AddDbContextFactory<OpenClawDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            services.AddSingleton<ISecretsStore, SecretsStore>();

            var provider = services.BuildServiceProvider();
            await using (var db = await provider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>().CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new VaultCliFixture(provider, root, dbPath, keysPath);
        }

        public Task<CliResult> RunCliAsync(params string[] args) => RunCliAsync(args, stdin: null);

        public async Task<CliResult> RunCliAsync(string[] args, string? stdin)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = RepositoryRoot
            };

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(CliProject);
            startInfo.ArgumentList.Add("--");
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            startInfo.Environment["VAULT_DB"] = DbPath;
            startInfo.Environment["VAULT_DATAPROTECTION_DIR"] = KeysPath;

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start vault CLI process.");
            if (stdin is not null)
            {
                await process.StandardInput.WriteLineAsync(stdin);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new CliResult(process.ExitCode, await stdoutTask, await stderrTask);
        }

        public void Dispose()
        {
            _provider.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(_root))
                    {
                        Directory.Delete(_root, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    Thread.Sleep(250);
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }

        private static string RepositoryRoot
        {
            get
            {
                var current = new DirectoryInfo(AppContext.BaseDirectory);
                while (current is not null)
                {
                    var candidate = Path.Combine(current.FullName, "src", "OpenClawNet.Cli.Vault", "OpenClawNet.Cli.Vault.csproj");
                    if (File.Exists(candidate))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root for vault CLI tests.");
            }
        }

        private static string CliProject =>
            Path.Combine(RepositoryRoot, "src", "OpenClawNet.Cli.Vault", "OpenClawNet.Cli.Vault.csproj");
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
