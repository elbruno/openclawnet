using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenClawNet.Storage.Azure;

public static class AzureStorageServiceCollectionExtensions
{
    public static IServiceCollection AddAzureKeyVaultSecretsStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureKeyVaultSecretsStoreOptions>()
            .Configure<IConfiguration>((opts, cfg) => cfg.GetSection(AzureKeyVaultSecretsStoreOptions.SectionName).Bind(opts));

        var vaultUri = configuration.GetValue<string>("Storage:Azure:KeyVault:Uri");
        if (string.IsNullOrWhiteSpace(vaultUri))
            throw new InvalidOperationException("Storage:Azure:KeyVault:Uri is required.");

        services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
        services.AddSingleton<AzureKeyVaultSecretsStore>();
        return services;
    }
}
