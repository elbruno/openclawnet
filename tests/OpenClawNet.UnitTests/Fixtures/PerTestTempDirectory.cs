using System;
using System.IO;

namespace OpenClawNet.UnitTests.Fixtures;

/// <summary>
/// Per-test isolated temp directory, created on construction and deleted on disposal.
///
/// Usage (xUnit):
///
///   public sealed class MyTests : IDisposable
///   {
///       private readonly PerTestTempDirectory _temp = new();
///
///       [Fact] public void Foo() { var p = _temp.GetPath("file.txt"); ... }
///
///       public void Dispose() => _temp.Dispose();
///   }
///
/// Each instance gets a unique path under <see cref="Path.GetTempPath"/> so concurrent
/// test execution (xUnit's default) cannot collide on shared file paths — the durable
/// fix tracked in elbruno/openclawnet-plan#94.
/// </summary>
public sealed class PerTestTempDirectory : IDisposable
{
    public string Path { get; }

    public PerTestTempDirectory(string? prefix = null)
    {
        var name = string.IsNullOrEmpty(prefix)
            ? Guid.NewGuid().ToString("N")
            : $"{prefix}-{Guid.NewGuid():N}";

        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        Directory.CreateDirectory(Path);
    }

    /// <summary>Combines a child path under this temp directory.</summary>
    public string GetPath(params string[] segments)
    {
        var all = new string[segments.Length + 1];
        all[0] = Path;
        Array.Copy(segments, 0, all, 1, segments.Length);
        return System.IO.Path.Combine(all);
    }

    public static implicit operator string(PerTestTempDirectory d) => d.Path;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; transient Windows file locks should not fail tests.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — cleanup is best-effort.
        }
    }
}
