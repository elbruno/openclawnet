using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.UnitTests.Fixtures;

public sealed class MockOllamaClient
{
    private readonly Mock<IModelClient> _mock;

    public MockOllamaClient()
    {
        _mock = new Mock<IModelClient>();
    }

    public IModelClient Object => _mock.Object;

    public void SetupAvailable(bool available)
    {
        _mock.Setup(m => m.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(available);
    }

    public void SetupAvailableThrows(Exception exception)
    {
        _mock.Setup(m => m.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }
}
