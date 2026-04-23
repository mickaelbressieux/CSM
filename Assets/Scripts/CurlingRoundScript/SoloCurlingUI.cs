using UnityEngine;
using TMPro;

public class SoloCurlingUI : MonoBehaviour
{
    public CurlingStoneController stone;
    public SoloCurlingGameManager gameManager;
    public TMP_Text infoText;

    private void Update()
    {
        if (stone == null || gameManager == null || infoText == null)
            return;

        if (!stone.HasBeenShot)
        {
            float power      = stone.GetCurrentPower();
            float curl       = stone.GetCurlAmount();
            float maxCurl    = stone.maxCurlPower;
            Vector3 aim      = stone.GetAimDirection();

            string curlBar = CurlBar(curl, maxCurl);

            infoText.text =
                "Left / Right: aim\n" +
                "Up / Down: power\n" +
                "Q: curl left   E: curl right\n" +
                "Space: shoot\n\n" +
                $"Power: {power:F1}\n" +
                $"Curl:  {curlBar} {curl:+0.0;-0.0;0.0}\n" +
                $"Aim: {aim.x:F2}, {aim.z:F2}";
        }
        else if (!stone.ShotFinished)
        {
            infoText.text = "The stone is sliding...";
        }
        else
        {
            infoText.text =
                $"Score: {gameManager.GetLastScore()}\n" +
                $"Distance: {gameManager.GetDistanceToCenter():F2}\n" +
                "Press R to reset";
        }
    }

    // Shows a centred bar: ←←←[.....] (left) or [.....]→→→ (right)
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