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
            float curlLeft   = stone.GetCurlLeft();
            float curlRight  = stone.GetCurlRight();
            float maxCurl    = stone.maxCurlPower;
            Vector3 aim      = stone.GetAimDirection();

            string leftBar  = GaugeBar(curlLeft,  maxCurl);
            string rightBar = GaugeBar(curlRight, maxCurl);

            infoText.text =
                "Left / Right: aim\n" +
                "Up / Down: power\n" +
                "Q / A: curl left\n" +
                "E / D: curl right\n" +
                "Space: shoot\n\n" +
                $"Power:      {power:F1}\n" +
                $"Curl Left:  {leftBar} {curlLeft:F1}\n" +
                $"Curl Right: {rightBar} {curlRight:F1}\n" +
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

    private string GaugeBar(float value, float max, int steps = 8)
    {
        int filled = Mathf.RoundToInt((value / max) * steps);
        filled = Mathf.Clamp(filled, 0, steps);
        return "[" + new string('|', filled) + new string('.', steps - filled) + "]";
    }
}