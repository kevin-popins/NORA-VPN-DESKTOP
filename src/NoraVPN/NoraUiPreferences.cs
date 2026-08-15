#if WINDOWS
using System.IO;
using System.Text.Json;

namespace Nvp;

internal static class NoraUiPreferences
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static bool CloseToTrayNoticeDismissed
    {
        get
        {
            lock (Sync)
            {
                try { return Load().CloseToTrayNoticeDismissed; }
                catch { return false; }
            }
        }
    }

    public static void SetCloseToTrayNoticeDismissed(bool dismissed)
    {
        lock (Sync)
        {
            var settings = Load();
            settings.CloseToTrayNoticeDismissed = dismissed;
            Save(settings);
        }
    }

    private static UiPreferencesFile Load()
    {
        var path = PreferencesPath();
        if (!File.Exists(path))
            return new UiPreferencesFile();
        try
        {
            return JsonSerializer.Deserialize<UiPreferencesFile>(File.ReadAllText(path), JsonOptions)
                   ?? new UiPreferencesFile();
        }
        catch
        {
            return new UiPreferencesFile();
        }
    }

    private static void Save(UiPreferencesFile settings)
    {
        var path = PreferencesPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string PreferencesPath() => Path.Combine(NoraAppState.DataRoot, "ui-preferences.json");

    internal static int RunSelfTest(TextWriter output)
    {
        var originalRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "nora-ui-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", temporaryRoot);
            var shownInitially = !CloseToTrayNoticeDismissed;
            SetCloseToTrayNoticeDismissed(true);
            var dismissedPersisted = CloseToTrayNoticeDismissed;

            // Active-profile persistence uses a different file and must never
            // erase the tray-notice choice.
            NoraAppState.SaveActiveProfilePath(Path.Combine(temporaryRoot, "fixture-profile.json"));
            var survivedProfileSave = CloseToTrayNoticeDismissed;
            SetCloseToTrayNoticeDismissed(false);
            var resetPersisted = !CloseToTrayNoticeDismissed;

            var passed = shownInitially && dismissedPersisted && survivedProfileSave && resetPersisted;
            output.WriteLine(passed
                ? "UI PREFERENCES SELF-TEST PASS: close-to-tray dismissal persists independently of active profile"
                : "UI PREFERENCES SELF-TEST FAIL");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            output.WriteLine("UI PREFERENCES SELF-TEST FAIL: " + exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORA_DATA_ROOT", originalRoot);
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch { }
        }
    }

    private sealed class UiPreferencesFile
    {
        public bool CloseToTrayNoticeDismissed { get; set; }
    }
}
#endif
