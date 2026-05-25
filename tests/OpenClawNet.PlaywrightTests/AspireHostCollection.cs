using Xunit;

namespace OpenClawNet.PlaywrightTests;

[CollectionDefinition("AspireHost")]
public sealed class AspireHostCollection : ICollectionFixture<AspireHostFixture>
{
}
