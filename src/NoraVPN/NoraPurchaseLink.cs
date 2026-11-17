#if WINDOWS
using System.IO;
using System.Diagnostics;

namespace Nvp;

internal static class NoraPurchaseLink
{
    internal static readonly Uri Website = new("https://nora.rest/", UriKind.Absolute);

    internal static void Open(Action<Uri>? openExternal = null)
    {
        if (openExternal is not null) openExternal(Website);
        else Process.Start(new ProcessStartInfo(Website.AbsoluteUri) { UseShellExecute = true });
    }

    internal static int RunSelfTest(TextWriter output)
    {
        var opened = new List<Uri>();
        Open(opened.Add);
        var passed = opened.Count == 1 && opened[0].AbsoluteUri == "https://nora.rest/" &&
                     opened[0].Scheme == Uri.UriSchemeHttps && opened[0].UserInfo.Length == 0 && opened[0].Query.Length == 0;
        output.WriteLine(passed ? "PASS: purchase handler dispatches the canonical public HTTPS website" : "FAIL: purchase website dispatch");
        return passed ? 0 : 1;
    }
}
#endif
