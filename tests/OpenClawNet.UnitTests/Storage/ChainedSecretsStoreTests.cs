using Moq;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Storage;

public sealed class ChainedSecretsStoreTests
{
    [Fact]
    public async Task GetAsync_ReturnsFirstNonNull()
    {
        var first = new Mock<ISecretsStore>(MockBehavior.Strict);
        var second = new Mock<ISecretsStore>(MockBehavior.Strict);
        var third = new Mock<ISecretsStore>(MockBehavior.Strict);

        first.Setup(s => s.GetAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        second.Setup(s => s.GetAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync("value");

        var store = new ChainedSecretsStore(new[] { first.Object, second.Object, third.Object });

        var result = await store.GetAsync("A");

        Assert.Equal("value", result);
        third.Verify(s => s.GetAsync("A", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_UsesFirstWritableStore()
    {
        var first = new Mock<ISecretsStore>(MockBehavior.Strict);
        var second = new Mock<ISecretsStore>(MockBehavior.Strict);

        first.Setup(s => s.SetAsync("A", "v", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());
        second.Setup(s => s.SetAsync("A", "v", null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = new ChainedSecretsStore(new[] { first.Object, second.Object });

        await store.SetAsync("A", "v");

        second.Verify(s => s.SetAsync("A", "v", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_UsesFirstWritableStore()
    {
        var first = new Mock<ISecretsStore>(MockBehavior.Strict);
        var second = new Mock<ISecretsStore>(MockBehavior.Strict);

        first.Setup(s => s.DeleteAsync("A", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());
        second.Setup(s => s.DeleteAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var store = new ChainedSecretsStore(new[] { first.Object, second.Object });

        var deleted = await store.DeleteAsync("A");

        Assert.True(deleted);
        second.Verify(s => s.DeleteAsync("A", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_DedupesByName_FirstWins()
    {
        var first = new Mock<ISecretsStore>(MockBehavior.Strict);
        var second = new Mock<ISecretsStore>(MockBehavior.Strict);

        first.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SecretSummary("A", "first", DateTime.UtcNow)
            });
        second.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SecretSummary("A", "second", DateTime.UtcNow),
                new SecretSummary("B", "second", DateTime.UtcNow)
            });

        var store = new ChainedSecretsStore(new[] { first.Object, second.Object });

        var list = await store.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("first", list.Single(item => item.Name == "A").Description);
        Assert.Contains(list, item => item.Name == "B");
    }
}
