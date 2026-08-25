using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options panel: master volume, fullscreen and screen size. Every control writes
/// straight through <see cref="GameSettings"/>, which applies the change as it stores it -
/// so there is no Apply button and nothing to revert.
///
/// Deliberately thin. Graphics quality, key rebinding and audio buses all belong here later;
/// each is another control plus another <see cref="GameSettings"/> property.
/// </summary>
public class OptionsPanel : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Button backButton;

    // Distinct screen sizes, in the same order as the dropdown's options. Refresh-rate
    // variants of one size are collapsed into a single entry - the player is picking a size.
    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    private void Awake()
    {
        BuildResolutionList();

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(Close);
        }
    }

    private void OnEnable()
    {
        RefreshFromSettings();
    }

    private void OnDisable()
    {
        GameSettings.Flush();
    }

    public void Close() => gameObject.SetActive(false);

    /// <summary>Show the stored settings without echoing them straight back into
    /// <see cref="GameSettings"/>, which is why every assignment here skips notification.</summary>
    private void RefreshFromSettings()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.SetValueWithoutNotify(CurrentResolutionIndex());
            resolutionDropdown.RefreshShownValue();
        }
    }

    private void BuildResolutionList()
    {
        resolutions.Clear();

        foreach (Resolution resolution in Screen.resolutions)
        {
            Vector2Int size = new Vector2Int(resolution.width, resolution.height);
            if (!resolutions.Contains(size))
            {
                resolutions.Add(size);
            }
        }

        // Screen.resolutions is empty on some headless/editor configurations; fall back to
        // the window we are actually running in so the dropdown is never blank.
        if (resolutions.Count == 0)
        {
            resolutions.Add(new Vector2Int(Screen.width, Screen.height));
        }

        if (resolutionDropdown == null)
        {
            return;
        }

        List<string> labels = new List<string>(resolutions.Count);
        foreach (Vector2Int size in resolutions)
        {
            labels.Add($"{size.x} x {size.y}");
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
    }

    private int CurrentResolutionIndex()
    {
        Vector2Int stored = GameSettings.HasStoredResolution
            ? GameSettings.Resolution
            : new Vector2Int(Screen.width, Screen.height);

        int index = resolutions.IndexOf(stored);
        // An unlisted size (a resized window, or a monitor that has since changed) falls back
        // to the largest entry rather than silently showing the wrong one.
        return index >= 0 ? index : resolutions.Count - 1;
    }

    private void OnMasterVolumeChanged(float value) => GameSettings.MasterVolume = value;

    private void OnFullscreenChanged(bool value) => GameSettings.Fullscreen = value;

    private void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= resolutions.Count)
        {
            return;
        }

        GameSettings.Resolution = resolutions[index];
    }
}
