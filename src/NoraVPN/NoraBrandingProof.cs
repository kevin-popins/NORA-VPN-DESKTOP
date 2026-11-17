#if WINDOWS
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingIcon = System.Drawing.Icon;
using DrawingSize = System.Drawing.Size;

namespace Nvp;

// Readback is restricted to this process's supplied WPF Window. Borrowed icon
// handles remain untouched; only CopyIcon/GetHicon-created handles are destroyed.
internal static class NoraBrandingProof
{
    internal static NoraBrandingProofResult Capture(Window ownWindow, string canonicalIconPath, string proofDirectory)
    {
        var hwnd = new WindowInteropHelper(ownWindow).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Own branding proof Window has no HWND.");
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId != Environment.ProcessId)
            throw new InvalidOperationException("Branding proof may read only its own process Window.");
        var canonicalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(canonicalIconPath))).ToLowerInvariant();
        Directory.CreateDirectory(proofDirectory);
        var productionBig = NoraWpfWindow.CreateTaskbarIconHandle();
        if (productionBig == IntPtr.Zero) throw new InvalidOperationException("Canonical production taskbar icon could not be created.");
        // Apply the same product-created BIG icon only after this HWND's
        // ownership has been verified. The returned previous handle is borrowed.
        var previousBig = IntPtr.Zero;
        var applied = false;
        try
        {
            previousBig = SendMessage(hwnd, 0x0080 /* WM_SETICON */, new IntPtr(1) /* ICON_BIG */, productionBig);
            applied = true;
            var big = Surface(hwnd, 1 /* ICON_BIG */, -14 /* GCLP_HICON */, "big", canonicalIconPath, proofDirectory);
            var small = Surface(hwnd, 0 /* ICON_SMALL */, -34 /* GCLP_HICONSM */, "small", canonicalIconPath, proofDirectory);
            return new NoraBrandingProofResult(hwnd.ToInt64(), Environment.ProcessId, processId, canonicalHash, big, small,
                previousBig.ToInt64(), productionBig.ToInt64(), true);
        }
        finally
        {
            if (applied)
                _ = SendMessage(hwnd, 0x0080 /* WM_SETICON */, new IntPtr(1) /* ICON_BIG */, previousBig);
            DestroyIcon(productionBig);
        }
    }

    private static NoraBrandingSurface Surface(IntPtr hwnd, int kind, int classIndex, string name, string canonicalPath, string directory)
    {
        var messageHandle = SendMessage(hwnd, 0x007F /* WM_GETICON */, new IntPtr(kind), IntPtr.Zero);
        var classHandle = messageHandle == IntPtr.Zero ? ClassIcon(hwnd, classIndex) : IntPtr.Zero;
        var handle = messageHandle == IntPtr.Zero ? classHandle : messageHandle;
        if (handle == IntPtr.Zero)
            return new NoraBrandingSurface(messageHandle.ToInt64(), classHandle.ToInt64(), 0, "missing", 0, 0, 1);
        using var actual = CloneBitmap(handle);
        using var canonicalFrame = new DrawingIcon(canonicalPath, new DrawingSize(actual.Width, actual.Height));
        using var canonicalBitmap = canonicalFrame.ToBitmap();
        // The same Windows HICON round-trip as the product icon smoke proof,
        // using the ICO frame selected for the actual native icon dimensions.
        var canonicalHandle = canonicalBitmap.GetHicon();
        try
        {
            using var expected = CloneBitmap(canonicalHandle);
            if (actual.Width != expected.Width || actual.Height != expected.Height)
                throw new InvalidOperationException("Canonical and native icon dimensions differ.");
            var mismatched = 0;
            for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
            {
                var left = actual.GetPixel(x, y);
                var right = expected.GetPixel(x, y);
                if (Math.Abs(left.A - right.A) + Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B) > 8)
                    mismatched++;
            }
            actual.Save(Path.Combine(directory, "branding-native-" + name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            expected.Save(Path.Combine(directory, "branding-canonical-" + name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            return new NoraBrandingSurface(messageHandle.ToInt64(), classHandle.ToInt64(), handle.ToInt64(),
                messageHandle == IntPtr.Zero ? "GetClassLongPtr" : "WM_GETICON", actual.Width, actual.Height,
                mismatched / Math.Max(1d, actual.Width * actual.Height));
        }
        finally { if (canonicalHandle != IntPtr.Zero) DestroyIcon(canonicalHandle); }
    }

    private static DrawingBitmap CloneBitmap(IntPtr borrowed)
    {
        var copy = CopyIcon(borrowed);
        if (copy == IntPtr.Zero) throw new InvalidOperationException("Native icon clone failed.");
        try
        {
            using var wrapper = DrawingIcon.FromHandle(copy);
            return wrapper.ToBitmap();
        }
        finally { DestroyIcon(copy); }
    }

    private static IntPtr ClassIcon(IntPtr hwnd, int index) => IntPtr.Size == 8
        ? GetClassLongPtr64(hwnd, index) : new IntPtr(unchecked((int)GetClassLong32(hwnd, index)));

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

internal sealed record NoraBrandingSurface(long WmGetIconHandle, long ClassIconHandle, long EffectiveHandle,
    string Source, int Width, int Height, double MismatchRatio)
{
    public bool Passed => EffectiveHandle != 0 && MismatchRatio <= 0.005;
}

internal sealed record NoraBrandingProofResult(long OwnWindowHandle, int ProcessId, uint NativeWindowProcessId,
    string CanonicalIconSha256, NoraBrandingSurface Big, NoraBrandingSurface Small,
    long PreviousBigHandle, long ProductionBigHandle,
    [property: JsonPropertyName("appliedProductionTaskbarPath")] bool AppliedProductionTaskbarPath)
{
    public bool Passed => AppliedProductionTaskbarPath && Big.Passed && Small.Passed;
}
#endif
