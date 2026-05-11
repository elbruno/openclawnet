using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Azure;

namespace OpenClawNet.UnitTests.Azure;

public sealed class AzureKeyVaultSecretsStoreTests
{
    [Fact]
    public async Task GetAsync_ReturnsSecretValue()
    {
        var secret = new KeyVaultSecret("name-with-dash", "value");
        var client = new FakeSecretClient((name, _) =>
            Task.FromResult(Response.FromValue(secret, new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        var value = await store.GetAsync("name.with_dash");

        Assert.Equal("value", value);
    }

    [Fact]
    public async Task GetAsync_MapsDotsAndUnderscoresToDashes()
    {
        var client = new FakeSecretClient((name, _) =>
            Task.FromResult(Response.FromValue(new KeyVaultSecret("name-with-dash", "value"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        await store.GetAsync("name.with_dash");

        Assert.Equal("name-with-dash", client.LastRequestedName);
    }

    [Fact]
    public async Task GetAsync_404_ReturnsNull()
    {
        var client = new FakeSecretClient((_, _) => Task.FromException<Response<KeyVaultSecret>>(new RequestFailedException(404, "not found")));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        var value = await store.GetAsync("missing");

        Assert.Null(value);
    }

    [Fact]
    public async Task GetAsync_RequestFailed_ThrowsVaultException()
    {
        var client = new FakeSecretClient((_, _) => Task.FromException<Response<KeyVaultSecret>>(new RequestFailedException(500, "boom")));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        await Assert.ThrowsAsync<VaultException>(() => store.GetAsync("boom"));
    }

    [Fact]
    public async Task GetAsync_InvalidName_ThrowsArgumentException()
    {
        var client = new FakeSecretClient((_, _) => Task.FromResult(Response.FromValue(new KeyVaultSecret("ignored", "value"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("bad$secret"));
    }

    [Fact]
    public async Task GetAsync_UsesCacheWithinTtl()
    {
        var client = new FakeSecretClient((name, _) =>
            Task.FromResult(Response.FromValue(new KeyVaultSecret("cached", "value"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions { CacheTtlMinutes = 30 }));

        var first = await store.GetAsync("cached");
        var second = await store.GetAsync("cached");

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task GetAsync_WithVersion_PassesVersionToKeyVault()
    {
        var client = new FakeSecretClient((name, version) =>
            Task.FromResult(Response.FromValue(new KeyVaultSecret(name, $"value-{version}"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        var value = await store.GetAsync("versioned_secret", version: 2);

        Assert.Equal("value-2", value);
        Assert.Equal("versioned-secret", client.LastRequestedName);
        Assert.Equal("2", client.LastRequestedVersion);
    }

    [Fact]
    public async Task RotateAsync_MapsToSetSecret_WithoutLiveKeyVault()
    {
        var client = new FakeSecretClient((name, _) =>
            Task.FromResult(Response.FromValue(new KeyVaultSecret(name, "unused"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        await store.RotateAsync("rotate_secret", "new-value");

        Assert.Equal("rotate-secret", client.LastSetName);
        Assert.Equal("new-value", client.LastSetValue);
    }

    [Fact]
    public async Task DeleteRecoverAndPurge_MapToKeyVaultLifecycleOperations()
    {
        var client = new FakeSecretClient((name, _) =>
            Task.FromResult(Response.FromValue(new KeyVaultSecret(name, "unused"), new FakeResponse())));
        var store = new AzureKeyVaultSecretsStore(client, Options.Create(new AzureKeyVaultSecretsStoreOptions()));

        Assert.True(await store.DeleteAsync("mapped_secret"));
        Assert.True(await store.RecoverAsync("mapped_secret"));
        Assert.True(await store.PurgeAsync("mapped_secret"));

        Assert.Equal("mapped-secret", client.LastDeletedName);
        Assert.Equal("mapped-secret", client.LastRecoveredName);
        Assert.Equal("mapped-secret", client.LastPurgedName);
    }

    private sealed class FakeSecretClient : SecretClient
    {
        private readonly Func<string, string?, Task<Response<KeyVaultSecret>>> _handler;

        public FakeSecretClient(Func<string, string?, Task<Response<KeyVaultSecret>>> handler)
            : base(new Uri("https://example.vault.azure.net/"), new DefaultAzureCredential())
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }
        public string? LastRequestedName { get; private set; }
        public string? LastRequestedVersion { get; private set; }
        public string? LastSetName { get; private set; }
        public string? LastSetValue { get; private set; }
        public string? LastDeletedName { get; private set; }
        public string? LastRecoveredName { get; private set; }
        public string? LastPurgedName { get; private set; }

        public override async Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name,
            string? version = null,
            SecretContentType? outContentType = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            LastRequestedName = name;
            LastRequestedVersion = version;
            return await _handler(name, version);
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            LastSetName = name;
            LastSetValue = value;
            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), new FakeResponse()));
        }

        public override Task<DeleteSecretOperation> StartDeleteSecretAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            LastDeletedName = name;
            return Task.FromResult<DeleteSecretOperation>(null!);
        }

        public override Task<RecoverDeletedSecretOperation> StartRecoverDeletedSecretAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            LastRecoveredName = name;
            return Task.FromResult<RecoverDeletedSecretOperation>(null!);
        }

        public override Task<Response> PurgeDeletedSecretAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            LastPurgedName = name;
            return Task.FromResult<Response>(new FakeResponse());
        }
    }

}
