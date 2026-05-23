using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenClawNet.Storage.Azure;

public sealed class AzureDataProtectionOptions
{
    public const string SectionName = "Storage:Azure:DataProtection";

    public string BlobUri { get; set; } = string.Empty;
    public string Container { get; set; } = "dataprotection";
    public string BlobName { get; set; } = "keys.xml";
    public string KeyVaultKeyUri { get; set; } = string.Empty;
}

public static class AzureDataProtectionExtensions
{
    public static IServiceCollection AddOpenClawNetAzureDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new AzureDataProtectionOptions();
        configuration.GetSection(AzureDataProtectionOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.BlobUri))
            throw new InvalidOperationException("Storage:Azure:DataProtection:BlobUri is required.");
        if (string.IsNullOrWhiteSpace(options.Container))
            throw new InvalidOperationException("Storage:Azure:DataProtection:Container is required.");
        if (string.IsNullOrWhiteSpace(options.BlobName))
            throw new InvalidOperationException("Storage:Azure:DataProtection:BlobName is required.");
        if (string.IsNullOrWhiteSpace(options.KeyVaultKeyUri))
            throw new InvalidOperationException("Storage:Azure:DataProtection:KeyVaultKeyUri is required.");

        var credential = new DefaultAzureCredential();
        var baseUri = new Uri(options.BlobUri, UriKind.Absolute);
        var containerSegment = options.Container.Trim().Trim('/');
        var blobSegment = options.BlobName.Trim().Trim('/');
        var blobUri = new Uri(baseUri, $"{containerSegment}/{blobSegment}");

        services.AddDataProtection()
            .SetApplicationName("OpenClawNet")
            .PersistKeysToAzureBlobStorage(blobUri, credential)
            .ProtectKeysWithAzureKeyVault(new Uri(options.KeyVaultKeyUri), credential);

        return services;
    }
}
