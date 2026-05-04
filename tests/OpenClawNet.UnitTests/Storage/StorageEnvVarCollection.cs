using Xunit;

namespace OpenClawNet.UnitTests.Storage;

/// <summary>
/// xunit collection for tests that mutate the global
/// <c>OPENCLAWNET_STORAGE_ROOT</c> environment variable. xunit parallelises
/// test classes by default; without a shared collection the env-var writes
/// in <see cref="OpenClawNetPathsScopeTests"/> bleed into
/// <see cref="OpenClawNetPathsTests"/> and produce flaky failures.
/// Serialising these classes into a single collection prevents the race.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StorageEnvVarCollection
{
    public const string Name = "StorageEnvVar";
}
