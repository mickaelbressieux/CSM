using UnityEngine;
using UnityEngine.InputSystem;

public class SoloCurlingGameManager : MonoBehaviour
{
    [Header("References")]
    public CurlingStoneController stone;
    public Transform houseCenter;

    [Header("Scoring")]
    public float ring1Radius = 1.0f;
    public float ring2Radius = 2.0f;
    public float ring3Radius = 3.0f;
    public float ring4Radius = 4.0f;

    private bool resultProcessed = false;
    private int lastScore = 0;

    private void Update()
    {
        if (stone == null || houseCenter == null)
            return;

        if (stone.ShotFinished && !resultProcessed)
        {
            lastScore = ComputeScore();
            resultProcessed = true;

            Debug.Log("Pierre arrêtée. Score = " + lastScore);
        }

        if (resultProcessed && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            stone.ResetStone();
            resultProcessed = false;
            lastScore = 0;
        }
    }

    private int ComputeScore()
    {
        Vector3 stonePos = stone.transform.position;
        Vector3 targetPos = houseCenter.position;

        stonePos.y = 0f;
        targetPos.y = 0f;

        float distance = Vector3.Distance(stonePos, targetPos);

        if (distance <= ring1Radius) return 4;
        if (distance <= ring2Radius) return 3;
        if (distance <= ring3Radius) return 2;
        if (distance <= ring4Radius) return 1;

        return 0;
    }

    public int GetLastScore()
    {
        return lastScore;
    }

    public bool HasResult()
    {
        return resultProcessed;
    }

    public float GetDistanceToCenter()
    {
        if (stone == null || houseCenter == null)
            return -1f;

        Vector3 stonePos = stone.transform.position;
        Vector3 targetPos = houseCenter.position;

        stonePos.y = 0f;
        targetPos.y = 0f;

        return Vector3.Distance(stonePos, targetPos);
    }
    }