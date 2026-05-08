using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage;

// ---- Configuration --------------------------------------------------------
string dbPath = Environment.GetEnvironmentVariable("VAULT_DB")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenClawNet", "vault.db");

string dataProtectionDir = Environment.GetEnvironmentVariable("VAULT_DATAPROTECTION_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenClawNet", "DataProtection-Keys");

if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

// ---- DI Setup -------------------------------------------------------------
var services = new ServiceCollection();
services.AddDbContextFactory<OpenClawDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDir));
services.AddSingleton<ISecretsStore, SecretsStore>();

var provider = services.BuildServiceProvider();
var store = provider.GetRequiredService<ISecretsStore>();
var dbFactory = provider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

// Ensure database exists
await using (var db = await dbFactory.CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
}

// ---- Command dispatch -----------------------------------------------------
string sub = args[0].ToLowerInvariant();

return sub switch
{
    "list" => await CmdList(store),
    "list-versions" => await CmdListVersions(store, args),
    "rotate" => await CmdRotate(store, args),
    "delete" => await CmdDelete(store, args),
    "recover" => await CmdRecover(store, args),
    "purge" => await CmdPurge(store, args),
    "audit-verify" => await CmdAuditVerify(dbFactory),
    _ => UnknownSub(sub),
};

static int UnknownSub(string sub)
{
    Console.Error.WriteLine($"Unknown subcommand: {sub}");
    PrintHelp();
    return 2;
}

// ---- Commands -------------------------------------------------------------

static async Task<int> CmdList(ISecretsStore store)
{
    var secrets = await store.ListAsync();
    if (secrets.Count == 0)
    {
        Console.WriteLine("(no secrets)");
        return 0;
    }

    Console.WriteLine($"{"Name",-30} {"Updated",-20} {"Description",-40}");
    Console.WriteLine(new string('-', 90));
    foreach (var secret in secrets)
    {
        var desc = secret.Description ?? "(no description)";
        Console.WriteLine($"{secret.Name,-30} {secret.UpdatedAt:yyyy-MM-dd HH:mm:ss,-20} {desc,-40}");
    }

    return 0;
}

static async Task<int> CmdListVersions(ISecretsStore store, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault-cli list-versions <name>");
        return 1;
    }

    string name = args[1];
    try
    {
        var versions = await store.ListVersionsAsync(name);
        if (versions.Count == 0)
        {
            Console.WriteLine($"(no versions found for '{name}')");
            return 0;
        }

        Console.WriteLine($"Versions for '{name}':");
        foreach (var version in versions)
        {
            Console.WriteLine($"  {version}");
        }
        return 0;
    }
    catch (NotSupportedException)
    {
        Console.Error.WriteLine("Version listing is not supported by the current secrets store.");
        return 1;
    }
}

static async Task<int> CmdRotate(ISecretsStore store, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault-cli rotate <name>");
        Console.Error.WriteLine("       (new value will be read from stdin)");
        return 1;
    }

    string name = args[1];
    Console.Write("Enter new secret value: ");
    string? newValue = Console.ReadLine();

    if (string.IsNullOrEmpty(newValue))
    {
        Console.Error.WriteLine("Error: new value cannot be empty.");
        return 1;
    }

    try
    {
        await store.RotateAsync(name, newValue);
        Console.WriteLine($"✓ Secret '{name}' rotated successfully.");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (NotSupportedException)
    {
        Console.Error.WriteLine("Rotation is not supported by the current secrets store.");
        return 1;
    }
}

static async Task<int> CmdDelete(ISecretsStore store, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault-cli delete <name>");
        return 1;
    }

    string name = args[1];
    bool deleted = await store.DeleteAsync(name);
    if (deleted)
    {
        Console.WriteLine($"✓ Secret '{name}' soft-deleted (can be recovered within 30 days).");
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"Secret '{name}' not found or already deleted.");
        return 1;
    }
}

static async Task<int> CmdRecover(ISecretsStore store, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault-cli recover <name>");
        return 1;
    }

    string name = args[1];
    try
    {
        bool recovered = await store.RecoverAsync(name);
        if (recovered)
        {
            Console.WriteLine($"✓ Secret '{name}' recovered successfully.");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"Secret '{name}' not found or not deleted.");
            return 1;
        }
    }
    catch (NotSupportedException)
    {
        Console.Error.WriteLine("Recovery is not supported by the current secrets store.");
        return 1;
    }
}

static async Task<int> CmdPurge(ISecretsStore store, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: vault-cli purge <name> --force");
        Console.Error.WriteLine("       (--force is required to confirm irreversible deletion)");
        return 1;
    }

    string name = args[1];
    bool force = args.Length > 2 && args[2] == "--force";

    if (!force)
    {
        Console.Error.WriteLine("Error: --force flag is required to purge a secret.");
        Console.Error.WriteLine("This operation is irreversible and will permanently delete all versions.");
        return 1;
    }

    bool purged = await store.PurgeAsync(name);
    if (purged)
    {
        Console.WriteLine($"✓ Secret '{name}' purged permanently (all versions deleted).");
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"Secret '{name}' not found.");
        return 1;
    }
}

static async Task<int> CmdAuditVerify(IDbContextFactory<OpenClawDbContext> dbFactory)
{
    await using var db = await dbFactory.CreateDbContextAsync();
    bool isValid = await SecretAccessAuditHashChain.VerifyAsync(db);

    if (isValid)
    {
        Console.WriteLine("✓ Audit hash-chain is valid (no tampering detected).");
        return 0;
    }
    else
    {
        Console.Error.WriteLine("✗ AUDIT CHAIN VERIFICATION FAILED");
        Console.Error.WriteLine("The audit trail has been tampered with or corrupted.");
        Console.Error.WriteLine("This is a SECURITY INCIDENT - investigation required.");
        return 1;
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
        OpenClawNet Vault CLI - Phase 5 Lifecycle Operations

        Usage: vault-cli <command> [options]

        Commands:
          list                       List all secrets (names, descriptions, timestamps only)
          list-versions <name>       List version numbers for a secret
          rotate <name>              Create new version of a secret (reads value from stdin)
          delete <name>              Soft-delete a secret (30-day recovery window)
          recover <name>             Recover a soft-deleted secret
          purge <name> --force       Permanently delete a secret and all versions (irreversible)
          audit-verify               Verify the audit hash-chain for tampering
          help, -h, --help           Show this help

        Environment Variables:
          VAULT_DB                   Path to vault database (default: %LOCALAPPDATA%\OpenClawNet\vault.db)
          VAULT_DATAPROTECTION_DIR   Path to DataProtection keys (default: %LOCALAPPDATA%\OpenClawNet\DataProtection-Keys)

        Security Notes:
          • Plaintext secret values are NEVER displayed by this CLI
          • All values are encrypted at rest using ASP.NET Core DataProtection
          • Rotate operations read new values from stdin to avoid shell history exposure
          • Audit verification detects any tampering with access logs

        Examples:
          vault-cli list
          vault-cli list-versions my-api-key
          vault-cli rotate my-api-key
          vault-cli delete my-api-key
          vault-cli recover my-api-key
          vault-cli purge my-api-key --force
          vault-cli audit-verify

        """);
}
