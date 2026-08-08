using UnityEngine;
using TMPro;

/// <summary>
/// Single UI authority for the curling scene (lives on the "UIManager" object). Both the
/// test-drop mode and the temp match mode render through this one styled <see cref="infoText"/>
/// label, so the HUD looks the same everywhere.
///
/// Two inputs drive it:
/// - the <b>active shot</b> (<see cref="stone"/> + <see cref="provider"/>) — the live aim / power /
///   curl HUD is derived from these. Test mode wires them once in the inspector; match mode
///   reassigns them each turn via <see cref="SetActiveShot"/>.
/// - an optional <b>banner</b> line (<see cref="SetBanner"/>) used for match turn prompts, the
///   "AI is throwing" state, and the end-of-round result.
/// </summary>
public class CurlingUIManager : MonoBehaviour
{
    public StoneLauncher stone;                // active shot state (HasBeenShot / ShotFinished)
    public PlayerShotProvider provider;        // active human aim / power / curl (null on AI turns)
    public SoloCurlingGameManager gameManager;
    public TMP_Text infoText;

    // Optional override / prefix line. Empty means "no banner".
    private string banner = "";

    /// <summary>Point the HUD at the shot currently in play. Pass a null provider when it is not a
    /// human's turn (e.g. the AI is throwing), so the aiming HUD is suppressed.</summary>
    public void SetActiveShot(StoneLauncher activeStone, PlayerShotProvider activeProvider)
    {
        stone = activeStone;
        provider = activeProvider;
    }

    public void SetBanner(string message) => banner = message ?? "";
    public void ClearBanner() => banner = "";

    private void Update()
    {
        if (infoText == null)
            return;

        string prefix = string.IsNullOrEmpty(banner) ? "" : banner + "\n\n";

        // Player is aiming: show the banner (if any) above the live aim / power / curl HUD.
        if (provider != null && stone != null && !stone.HasBeenShot)
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
        ShotData shot = provider.CurrentShot;
        float power   = shot.Power;
        float curl    = shot.Curl;
        float maxCurl = provider.maxCurlPower;
        Vector3 aim   = shot.Direction;

        string curlBar = CurlBar(curl, maxCurl);

        return
            "Left / Right: aim\n" +
            "Up / Down: power\n" +
            "Q: curl left   E: curl right\n" +
            "Space: shoot\n\n" +
            $"Power: {power:F1}\n" +
            $"Curl:  {curlBar} {curl:+0.0;-0.0;0.0}\n" +
            $"Aim: {aim.x:F2}, {aim.z:F2}";
    }

    // Shows a centred bar: <<<..|..... (left) or .....|..>>> (right)
    private string CurlBar(float value, float max, int halfSteps = 5)
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
