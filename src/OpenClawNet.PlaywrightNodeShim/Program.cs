using System.ComponentModel;
using System.Diagnostics;

var nodePath = Environment.GetEnvironmentVariable("OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE")
    ?? Path.Combine(@"C:\Program Files", "nodejs", "node.exe");

if (!File.Exists(nodePath))
{
    Console.Error.WriteLine($"System node.exe was not found at '{nodePath}'.");
    return 1;
}

var workingDirectory = Path.GetDirectoryName(nodePath) ?? Environment.CurrentDirectory;

try
{
    using var directProcess = StartNode(nodePath, workingDirectory, args);
    directProcess.WaitForExit();
    return directProcess.ExitCode;
}
catch (Win32Exception)
{
    using var shellProcess = StartNodeThroughCmd(nodePath, workingDirectory, args);
    shellProcess.WaitForExit();
    return shellProcess.ExitCode;
}

static Process StartNode(string executable, string workingDirectory, string[] args)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        WorkingDirectory = workingDirectory,
    };

    foreach (var arg in args)
    {
        startInfo.ArgumentList.Add(arg);
    }

    return Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start node.exe shim target '{executable}'.");
}

static Process StartNodeThroughCmd(string executable, string workingDirectory, string[] args)
{
    var startInfo = new ProcessStartInfo("cmd.exe")
    {
        UseShellExecute = false,
        WorkingDirectory = workingDirectory,
    };

    startInfo.ArgumentList.Add("/c");
    startInfo.ArgumentList.Add(executable);
    foreach (var arg in args)
    {
        startInfo.ArgumentList.Add(arg);
    }

    return Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start node.exe shim target through cmd.exe '{executable}'.");
}
