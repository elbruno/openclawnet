using System.Diagnostics;

var nodePath = Environment.GetEnvironmentVariable("OPENCLAWNET_PLAYWRIGHT_SYSTEM_NODE")
    ?? Path.Combine(@"C:\Program Files", "nodejs", "node.exe");

if (!File.Exists(nodePath))
{
    Console.Error.WriteLine($"System node.exe was not found at '{nodePath}'.");
    return 1;
}

var startInfo = new ProcessStartInfo(nodePath)
{
    UseShellExecute = false,
    WorkingDirectory = Environment.CurrentDirectory,
};

foreach (var arg in args)
{
    startInfo.ArgumentList.Add(arg);
}

using var process = Process.Start(startInfo)
    ?? throw new InvalidOperationException($"Failed to start node.exe shim target '{nodePath}'.");

process.WaitForExit();
return process.ExitCode;
