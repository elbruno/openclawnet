using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Storage.Azure;

namespace OpenClawNet.UnitTests.Azure;

public sealed class AzureDataProtectionTests
{
    [Fact]
    public void AddOpenClawNetAzureDataProtection_RegistersProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Azure:DataProtection:BlobUri"] = "https://account.blob.core.windows.net/",
                ["Storage:Azure:DataProtection:Container"] = "dataprotection",
                ["Storage:Azure:DataProtection:BlobName"] = "keys.xml",
                ["Storage:Azure:DataProtection:KeyVaultKeyUri"] = "https://vault.vault.azure.net/keys/dataprotection-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOpenClawNetAzureDataProtection(config);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDataProtectionProvider>();

        Assert.NotNull(provider);
    }
}
