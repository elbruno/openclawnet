using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Services;

internal static class SecretsImportCommand
{
    public static async Task<bool> TryRunAsync(string[] args, IServiceProvider services, CancellationToken ct = default)
    {
        if (args.Length < 4 || !args[0].Equals("secrets", StringComparison.OrdinalIgnoreCase)
            || !args[1].Equals("import", StringComparison.OrdinalIgnoreCase)
            || !args[2].Equals("--from", StringComparison.OrdinalIgnoreCase)
            || !args[3].Equals("user-secrets", StringComparison.OrdinalIgnoreCase))
            return false;

        var project = GetOption(args, "--project") ?? Directory.GetCurrentDirectory();
        var count = await ImportUserSecretsAsync(project, services, ct).ConfigureAwait(false);
        Console.WriteLine($"Imported {count} user secret(s) into the vault.");
        return true;
    }

    internal static async Task<int> ImportUserSecretsAsync(string projectPath, IServiceProvider services, CancellationToken ct = default)
    {
        var userSecretsId = ResolveUserSecretsId(projectPath);
        var secretsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "UserSecrets",
            userSecretsId,
            "secrets.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(secretsPath, optional: true)
            .Build();

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISecretsStore>();
        var auditor = scope.ServiceProvider.GetRequiredService<ISecretAccessAuditor>();

        var imported = 0;
        foreach (var pair in configuration.AsEnumerable().Where(p => !string.IsNullOrEmpty(p.Value)))
        {
            var vaultName = pair.Key.Replace(':', '/');
            await store.SetAsync(vaultName, pair.Value!, "Imported from .NET user-secrets", ct).ConfigureAwait(false);
            await auditor.RecordAsync(
                vaultName,
                new VaultCallerContext(VaultCallerType.Cli, "openclawnet secrets import", null),
                success: true,
                ct).ConfigureAwait(false);
            imported++;
        }

        return imported;
    }

    private static string ResolveUserSecretsId(string projectPath)
    {
        var path = Directory.Exists(projectPath)
            ? Directory.EnumerateFiles(projectPath, "*.csproj").FirstOrDefault()
            : projectPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("Project file with UserSecretsId was not found.");

        var doc = XDocument.Load(path);
        var id = doc.Descendants("UserSecretsId").FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Project file does not declare a UserSecretsId.");

        return id.Trim();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
