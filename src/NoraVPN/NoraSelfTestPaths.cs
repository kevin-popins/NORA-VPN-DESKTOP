#if WINDOWS
using System.IO;

namespace Nvp;

// Isolated entry points may use an explicitly owned workspace for temporary
// fixtures when the host's general TEMP directory is outside their sandbox.
internal static class NoraSelfTestPaths
{
    internal static string BaseDirectory => Path.GetFullPath(
        Environment.GetEnvironmentVariable("NORA_SELFTEST_ROOT") is { Length: > 0 } root ? root : Path.GetTempPath());
}
#endif
