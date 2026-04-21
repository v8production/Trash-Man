using UnityEngine;

/// <summary>
/// Lightweight runtime-gated logging for input/network debugging.
/// Safe to leave in builds; toggle via PlayerPrefs key "InputDebug".
/// </summary>
public static class InputDebug
{
    public const string Prefix = "[InputDebug]";
    private const string PrefKey = "InputDebug";

    // Default: enabled in debug/development builds.
    public static bool Enabled
    {
        get
        {
            if (Debug.isDebugBuild)
                return true;

            return PlayerPrefs.GetInt(PrefKey, 0) == 1;
        }
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static void Log(string message)
    {
        if (!Enabled)
            return;

        Debug.Log($"{Prefix} {message}");
    }

    public static void LogWarning(string message)
    {
        if (!Enabled)
            return;

        Debug.LogWarning($"{Prefix} {message}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void BootLog()
    {
        // Always print once so we can confirm the build has InputDebug compiled in.
        Debug.Log($"{Prefix} Boot Debug.isDebugBuild={Debug.isDebugBuild} Enabled={Enabled}");
    }
}
