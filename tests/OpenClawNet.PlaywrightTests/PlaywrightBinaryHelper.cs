using OpenClawNet.ServiceDefaults;

namespace OpenClawNet.PlaywrightTests;

internal static class PlaywrightBinaryHelper
{
    internal static void UnblockPlaywrightBinaries()
        => PlaywrightRuntimeHelper.PrepareForCurrentProcess();
}
