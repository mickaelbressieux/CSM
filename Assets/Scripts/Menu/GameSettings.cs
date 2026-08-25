using UnityEngine;

/// <summary>
/// The player's display and audio preferences, backed by PlayerPrefs. Each setter both
/// stores the value and applies it immediately, so the options UI never needs an
/// apply/confirm step - it just assigns.
///
/// <see cref="Apply"/> runs automatically on every play session via
/// <see cref="RuntimeInitializeOnLoadMethod"/> (the same idiom <see cref="MatchEvents"/>
/// uses to reset its statics), so saved preferences take effect no matter which scene you
/// press Play from - not only when you come through the main menu.
///
/// The screen size is stored as width/height rather than as an index into
/// <see cref="Screen.resolutions"/>, because that list changes with the monitor and a
/// stored index would silently start meaning something else.
/// </summary>
public static class GameSettings
{
    private const string MasterVolumeKey = "settings.masterVolume";
    private const string FullscreenKey = "settings.fullscreen";
    private const string ResolutionWidthKey = "settings.resolutionWidth";
    private const string ResolutionHeightKey = "settings.resolutionHeight";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
            AudioListener.volume = clamped;
        }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            Screen.fullScreen = value;
        }
    }

    /// <summary>Zero when no resolution has been chosen yet, in which case the platform
    /// default is left alone.</summary>
    public static Vector2Int Resolution
    {
        get => new Vector2Int(
            PlayerPrefs.GetInt(ResolutionWidthKey, 0),
            PlayerPrefs.GetInt(ResolutionHeightKey, 0));
        set
        {
            if (value.x <= 0 || value.y <= 0)
            {
                return;
            }

            PlayerPrefs.SetInt(ResolutionWidthKey, value.x);
            PlayerPrefs.SetInt(ResolutionHeightKey, value.y);
            Screen.SetResolution(value.x, value.y, Fullscreen);
        }
    }

    public static bool HasStoredResolution => PlayerPrefs.HasKey(ResolutionWidthKey);

    /// <summary>Push every stored preference onto the running game.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        if (HasStoredResolution)
        {
            Vector2Int stored = Resolution;
            // One call so the mode change and the size change land together; a separate
            // Screen.fullScreen assignment first would resize twice.
            Screen.SetResolution(stored.x, stored.y, Fullscreen);
        }
        else
        {
            Screen.fullScreen = Fullscreen;
        }
    }

    /// <summary>Flush to disk. PlayerPrefs saves on quit anyway, but a crash mid-session
    /// would otherwise lose the change.</summary>
    public static void Flush() => PlayerPrefs.Save();
}
