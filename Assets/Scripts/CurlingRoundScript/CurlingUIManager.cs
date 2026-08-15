using UnityEngine;
using TMPro;

/// <summary>
/// Single UI authority for the curling scene (lives on the "UIManager" object). Both the
/// test-drop mode and the temp match mode render through this one styled <see cref="infoText"/>
/// label, so the HUD looks the same everywhere.
///
/// Two inputs drive it:
/// - the <b>active shot</b> — the live aim / power / curl HUD is derived from it. Both modes
///   hand it over via <see cref="SetActiveShot"/> each time a stone is spawned.
/// - an optional <b>banner</b> line (<see cref="SetBanner"/>) used for match turn prompts, the
///   "AI is throwing" state, and the end-of-round result.
/// </summary>
public class CurlingUIManager : MonoBehaviour
{
    public StoneLauncher stone;                // active shot state (HasBeenShot / ShotFinished)
    public SoloCurlingGameManager gameManager;
    public TMP_Text infoText;

    // Optional override / prefix line. Empty means "no banner".
    private string banner = "";

    // The provider actually driving the HUD. Supplied by the game manager through SetActiveShot
    // each time a stone is spawned, and typed to the interface so any IShotProvider (player or
    // AI) works without the HUD naming a concrete type. Null until the first stone exists.
    private IShotProvider activeProvider;

    /// <summary>Point the HUD at the shot currently in play. Pass a null provider when it is not a
    /// human's turn (e.g. the AI is throwing), so the aiming HUD is suppressed.</summary>
    public void SetActiveShot(StoneLauncher activeStone, IShotProvider activeShotProvider)
    {
        stone = activeStone;
        activeProvider = activeShotProvider;
    }

    public void SetBanner(string message) => banner = message ?? "";
    public void ClearBanner() => banner = "";

    private void Update()
    {
        if (infoText == null)
            return;

        string prefix = string.IsNullOrEmpty(banner) ? "" : banner + "\n\n";

        // Player is aiming: show the banner (if any) above the live aim / power / curl HUD.
        if (activeProvider != null && stone != null && !stone.HasBeenShot)
        {
            infoText.text = prefix + AimingHud();
            return;
        }

        // A shot is sliding.
        if (stone != null && stone.HasBeenShot && !stone.ShotFinished)
        {
            infoText.text = string.IsNullOrEmpty(banner) ? "The stone is sliding..." : banner;
            return;
        }

        // Otherwise: a banner (AI turn text / match result) wins; else the test-mode result panel.
        if (!string.IsNullOrEmpty(banner))
        {
            infoText.text = banner;
            return;
        }

        infoText.text = gameManager != null
            ? $"Score: {gameManager.GetLastScore()}\n" +
              $"Distance: {gameManager.GetDistanceToCenter():F2}\n" +
              "Press R to reset"
            : "";
    }

    private string AimingHud()
    {
        ShotData shot    = activeProvider.CurrentShot;
        float power      = shot.Power;
        float curl       = shot.Curl;
        float maxCurl    = activeProvider.MaxCurl;
        float lateral    = shot.LateralOffset;
        float maxLateral = activeProvider.MaxLateral;
        Vector3 aim      = shot.Direction;

        string curlBar    = SignedBar(curl, maxCurl);
        string lateralBar = SignedBar(lateral, maxLateral);

        return
            "Left / Right: aim\n" +
            "Up / Down: power\n" +
            "Q: curl left   E: curl right\n" +
            "A: offset left   D: offset right\n" +
            "Space: shoot\n\n" +
            $"Power:  {power:F1}\n" +
            $"Curl:   {curlBar} {curl:+0.0;-0.0;0.0}\n" +
            $"Offset: {lateralBar} {lateral:+0.0;-0.0;0.0}\n" +
            $"Aim: {aim.x:F2}, {aim.z:F2}";
    }

    // Shows a centred bar: <<<..|..... (left) or .....|..>>> (right).
    // Generic over any signed value/max pair — used for both curl and lateral offset.
    private string SignedBar(float value, float max, int halfSteps = 5)
    {
        int filled = Mathf.RoundToInt((Mathf.Abs(value) / max) * halfSteps);
        filled = Mathf.Clamp(filled, 0, halfSteps);
        string empty = new string('.', halfSteps);
        if (value < -0.05f)
        {
            string arrows = new string('<', filled);
            return arrows + new string('.', halfSteps - filled) + "|" + empty;
        }
        if (value > 0.05f)
        {
            string arrows = new string('>', filled);
            return empty + "|" + new string('.', halfSteps - filled) + arrows;
        }
        return empty + "|" + empty;
    }
}
