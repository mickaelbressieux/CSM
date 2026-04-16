using System.Collections.Generic;
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class AIStoneController : MonoBehaviour
{
    private enum StoneAction
    {
        TargestNearestEnnemy,
        TargestCenter
    }

    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string centerTag = "Center";
    [Header("UI")]
    [SerializeField] private bool showDistanceOnScreen = true;
    [SerializeField] private Vector2 uiMargin = new Vector2(16f, 16f);
    [SerializeField] private int fontSize = 20;

    [Header("Actions")]
    [SerializeField] private List<StoneAction> availableActions = new List<StoneAction>
    {
        StoneAction.TargestNearestEnnemy,
        StoneAction.TargestCenter
    };
    [SerializeField] private int selectedActionIndex;

    [Header("Launch")]
    [SerializeField] private float launchForce = 10f;
    [SerializeField] private bool useDistanceBasedForce = true;
    [SerializeField] private ForceMode forceMode = ForceMode.Impulse;

    [Header("Empirical Model (force -> distance)")]
    [SerializeField] private float[] measuredForces =
    {
        12f, 17f, 22f, 27f, 32f, 37f,
        40.31f, 44.20f, 47.40f, 50.20f, 52.71f, 55.00f,
        57.12f, 59.09f, 60.95f, 62.71f, 64.39f, 66.00f,
        68.55f, 70.05f, 69.50f, 70.90f, 72.26f
    };
    [SerializeField] private float[] measuredDistances =
    {
        1.6f, 5f, 9.3f, 15f, 23.7f, 33f,
        40f, 50f, 60f, 70f, 80f, 90f,
        100f, 110f, 120f, 130f, 140f, 150f,
        160f, 170f, 180f, 190f, 200f
    };

    public float LastDistance { get; private set; } = -1f;
    public GameObject LastNearestPlayer { get; private set; }
    public GameObject LastNearestCenter { get; private set; }
    public float LastAppliedForce { get; private set; } = -1f;
    public int EnterActivationCount { get; private set; }

    private GUIStyle distanceStyle;
    private bool warnedMissingPlayerTag;
    private bool warnedMissingCenterTag;
    private Rigidbody rb;

    private void Awake()
    {
        distanceStyle = new GUIStyle();
        distanceStyle.alignment = TextAnchor.UpperRight;
        distanceStyle.fontSize = fontSize;
        distanceStyle.normal.textColor = Color.white;

        rb = GetComponent<Rigidbody>();
        EnsureAvailableActionsConfigured();
        ClampSelectedActionIndex();
    }

    private void OnValidate()
    {
        EnsureAvailableActionsConfigured();
        ClampSelectedActionIndex();
    }

    private void Update()
    {
        HandleActionSelectionInput();

        if (!WasEnterPressedThisFrame())
            return;

        EnterActivationCount++;
        ExecuteSelectedAction();
    }

    private bool WasEnterPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            pressed = Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
#endif

        if (pressed)
            return true;

        // Fallback for projects/scenes still reading legacy keys.
        try
        {
            pressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        }
        catch (InvalidOperationException)
        {
            pressed = false;
        }

        return pressed;
    }

    private bool WasNextActionPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            pressed = Keyboard.current.numpadPlusKey.wasPressedThisFrame || (shift && Keyboard.current.equalsKey.wasPressedThisFrame);
        }
#endif

        if (pressed)
            return true;

        try
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            pressed = Input.GetKeyDown(KeyCode.KeypadPlus) || (shift && Input.GetKeyDown(KeyCode.Equals));
        }
        catch (InvalidOperationException)
        {
            pressed = false;
        }

        return pressed;
    }

    private bool WasPreviousActionPressedThisFrame()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            pressed = Keyboard.current.numpadMinusKey.wasPressedThisFrame || Keyboard.current.minusKey.wasPressedThisFrame;
#endif

        if (pressed)
            return true;

        try
        {
            pressed = Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus);
        }
        catch (InvalidOperationException)
        {
            pressed = false;
        }

        return pressed;
    }

    private void HandleActionSelectionInput()
    {
        EnsureAvailableActionsConfigured();

        if (availableActions == null || availableActions.Count == 0)
            return;

        if (WasNextActionPressedThisFrame())
        {
            selectedActionIndex = (selectedActionIndex + 1) % availableActions.Count;
            Debug.Log("Action selectionnee: " + GetCurrentActionName(), this);
        }
        else if (WasPreviousActionPressedThisFrame())
        {
            selectedActionIndex = (selectedActionIndex - 1 + availableActions.Count) % availableActions.Count;
            Debug.Log("Action selectionnee: " + GetCurrentActionName(), this);
        }
    }

    private void ExecuteSelectedAction()
    {
        if (availableActions == null || availableActions.Count == 0)
            return;

        ClampSelectedActionIndex();
        StoneAction action = availableActions[selectedActionIndex];

        switch (action)
        {
            case StoneAction.TargestNearestEnnemy:
                ComputeDistanceToNearestPlayer();
                LaunchTowardNearestPlayer();
                break;
            case StoneAction.TargestCenter:
                ComputeDistanceToNearestCenter();
                LaunchTowardNearestCenter();
                break;
        }
    }

    public void ComputeDistanceToNearestPlayer()
    {
        GameObject[] players = FindPlayersWithAssignedTag();

        if (players == null || players.Length == 0)
        {
            LastDistance = -1f;
            LastNearestPlayer = null;
            Debug.LogWarning("Aucun objet avec le tag '" + playerTag + "' n'a ete trouve.", this);
            return;
        }

        Vector3 myPosition = transform.position;
        float nearestSqrDistance = float.MaxValue;
        GameObject nearest = null;

        foreach (GameObject player in players)
        {
            if (player == null || player == gameObject)
                continue;

            float sqrDistance = (player.transform.position - myPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = player;
            }
        }

        if (nearest == null)
        {
            LastDistance = -1f;
            LastNearestPlayer = null;
            Debug.LogWarning("Aucun objet valide avec le tag '" + playerTag + "' n'a ete trouve.", this);
            return;
        }

        LastNearestPlayer = nearest;
        LastDistance = Mathf.Sqrt(nearestSqrDistance);

        Debug.Log("Distance vers le player le plus proche ('" + nearest.name + "') : " + LastDistance, this);
    }

    public void ComputeDistanceToNearestCenter()
    {
        GameObject[] centers = FindCentersWithAssignedTag();

        if (centers == null || centers.Length == 0)
        {
            LastDistance = -1f;
            LastNearestCenter = null;
            Debug.LogWarning("Aucun objet avec le tag '" + centerTag + "' n'a ete trouve.", this);
            return;
        }

        Vector3 myPosition = transform.position;
        float nearestSqrDistance = float.MaxValue;
        GameObject nearest = null;

        foreach (GameObject center in centers)
        {
            if (center == null || center == gameObject)
                continue;

            float sqrDistance = (center.transform.position - myPosition).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
            {
                nearestSqrDistance = sqrDistance;
                nearest = center;
            }
        }

        if (nearest == null)
        {
            LastDistance = -1f;
            LastNearestCenter = null;
            Debug.LogWarning("Aucun objet valide avec le tag '" + centerTag + "' n'a ete trouve.", this);
            return;
        }

        LastNearestCenter = nearest;
        LastDistance = Mathf.Sqrt(nearestSqrDistance);

        Debug.Log("Distance vers le center le plus proche ('" + nearest.name + "') : " + LastDistance, this);
    }

    private GameObject[] FindPlayersWithAssignedTag()
    {
        if (!IsTagDefined(playerTag))
        {
            LastDistance = -1f;
            LastNearestPlayer = null;
            if (!warnedMissingPlayerTag)
            {
                warnedMissingPlayerTag = true;
                Debug.LogWarning("Le tag '" + playerTag + "' n'existe pas dans Tags and Layers.", this);
            }
            return new GameObject[0];
        }

        warnedMissingPlayerTag = false;
        List<GameObject> taggedPlayers = new List<GameObject>();
        PlayerTagAssigner[] assigners = FindObjectsOfType<PlayerTagAssigner>();

        foreach (PlayerTagAssigner assigner in assigners)
        {
            if (assigner == null)
                continue;

            GameObject candidate = assigner.gameObject;
            if (candidate != null && candidate.CompareTag(playerTag))
                taggedPlayers.Add(candidate);
        }

        if (taggedPlayers.Count > 0)
            return taggedPlayers.ToArray();

        return GameObject.FindGameObjectsWithTag(playerTag);
    }

    private GameObject[] FindCentersWithAssignedTag()
    {
        if (!IsTagDefined(centerTag))
        {
            LastDistance = -1f;
            LastNearestCenter = null;
            if (!warnedMissingCenterTag)
            {
                warnedMissingCenterTag = true;
                Debug.LogWarning("Le tag '" + centerTag + "' n'existe pas dans Tags and Layers.", this);
            }
            return new GameObject[0];
        }

        warnedMissingCenterTag = false;
        return GameObject.FindGameObjectsWithTag(centerTag);
    }

    private bool IsTagDefined(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        if (tagName == "Untagged")
            return true;

        try
        {
            GameObject.FindWithTag(tagName);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private void LaunchTowardNearestPlayer()
    {
        if (rb == null)
        {
            Debug.LogWarning("Rigidbody manquant: propulsion annulee.", this);
            return;
        }

        GameObject nearestPlayer = LastNearestPlayer;
        if (nearestPlayer == null)
        {
            Debug.LogWarning("Aucun player detecte: propulsion annulee.", this);
            return;
        }

        Vector3 direction = nearestPlayer.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("Player trop proche ou meme position: propulsion annulee.", this);
            return;
        }

        direction.Normalize();

        float finalForce = useDistanceBasedForce
            ? ComputeForceForDistance(LastDistance)
            : launchForce;

        LastAppliedForce = finalForce;
        rb.AddForce(direction * finalForce, forceMode);
        Debug.Log("Propulsion vers player appliquee. Distance recue: " + LastDistance + " | Force: " + finalForce, this);
    }

    private void LaunchTowardNearestCenter()
    {
        if (rb == null)
        {
            Debug.LogWarning("Rigidbody manquant: propulsion annulee.", this);
            return;
        }

        GameObject nearestCenter = LastNearestCenter;
        if (nearestCenter == null)
        {
            Debug.LogWarning("Aucun center detecte: propulsion annulee.", this);
            return;
        }

        Vector3 direction = nearestCenter.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("Center trop proche ou meme position: propulsion annulee.", this);
            return;
        }

        direction.Normalize();

        float finalForce = useDistanceBasedForce
            ? ComputeForceForDistance(LastDistance)
            : launchForce;

        finalForce = Mathf.Max(0f, finalForce - 7f);

        LastAppliedForce = finalForce;
        rb.AddForce(direction * finalForce, forceMode);
        Debug.Log("Propulsion vers center appliquee. Distance recue: " + LastDistance + " | Force: " + finalForce, this);
    }

    private float ComputeForceForDistance(float targetDistance)
    {
        if (targetDistance <= 0f)
            return launchForce;

        if (!HasValidModelData())
            return launchForce;

        int last = measuredDistances.Length - 1;

        if (targetDistance <= measuredDistances[0])
            return InterpolateForce(targetDistance, 0, 1);

        if (targetDistance >= measuredDistances[last])
            return measuredForces[last];

        for (int i = 0; i < last; i++)
        {
            if (targetDistance >= measuredDistances[i] && targetDistance <= measuredDistances[i + 1])
                return InterpolateForce(targetDistance, i, i + 1);
        }

        return launchForce;
    }

    private float InterpolateForce(float targetDistance, int i0, int i1)
    {
        float d0 = measuredDistances[i0];
        float d1 = measuredDistances[i1];
        float f0 = measuredForces[i0];
        float f1 = measuredForces[i1];

        if (Mathf.Approximately(d0, d1))
            return f0;

        float t = Mathf.InverseLerp(d0, d1, targetDistance);
        return Mathf.Lerp(f0, f1, t);
    }

    private bool HasValidModelData()
    {
        if (measuredForces == null || measuredDistances == null)
            return false;

        if (measuredForces.Length < 2 || measuredForces.Length != measuredDistances.Length)
            return false;

        for (int i = 1; i < measuredDistances.Length; i++)
        {
            if (measuredDistances[i] <= measuredDistances[i - 1])
                return false;
        }

        return true;
    }

    private void ClampSelectedActionIndex()
    {
        if (availableActions == null || availableActions.Count == 0)
        {
            selectedActionIndex = 0;
            return;
        }

        if (selectedActionIndex < 0)
            selectedActionIndex = 0;

        if (selectedActionIndex >= availableActions.Count)
            selectedActionIndex = availableActions.Count - 1;
    }

    private void EnsureAvailableActionsConfigured()
    {
        if (availableActions == null)
            availableActions = new List<StoneAction>();

        if (!availableActions.Contains(StoneAction.TargestNearestEnnemy))
            availableActions.Add(StoneAction.TargestNearestEnnemy);

        if (!availableActions.Contains(StoneAction.TargestCenter))
            availableActions.Add(StoneAction.TargestCenter);
    }

    private string GetCurrentActionName()
    {
        if (availableActions == null || availableActions.Count == 0)
            return "Aucune";

        ClampSelectedActionIndex();
        return availableActions[selectedActionIndex].ToString();
    }

    private void OnGUI()
    {
        if (!showDistanceOnScreen)
            return;

        if (distanceStyle == null)
            Awake();

        string label = LastDistance >= 0f
            ? "Distance player proche : " + LastDistance.ToString("F2") + " m"
            : "Distance player proche : NA";

        string forceLabel = LastAppliedForce >= 0f
            ? "Force appliquee : " + LastAppliedForce.ToString("F2")
            : "Force appliquee : NA";

        string actionLabel = "Action active (+/-) : " + GetCurrentActionName();

        Vector2 sizeDistance = distanceStyle.CalcSize(new GUIContent(label));
        Vector2 sizeForce = distanceStyle.CalcSize(new GUIContent(forceLabel));
        Vector2 sizeAction = distanceStyle.CalcSize(new GUIContent(actionLabel));
        float width = Mathf.Max(sizeDistance.x, Mathf.Max(sizeForce.x, sizeAction.x));
        float lineHeight = Mathf.Max(sizeDistance.y, Mathf.Max(sizeForce.y, sizeAction.y));
        Rect rect = new Rect(
            Screen.width - width - uiMargin.x,
            uiMargin.y,
            width,
            lineHeight);

        Rect forceRect = new Rect(
            Screen.width - width - uiMargin.x,
            uiMargin.y + lineHeight,
            width,
            lineHeight);

        Rect actionRect = new Rect(
            Screen.width - width - uiMargin.x,
            uiMargin.y + (lineHeight * 2f),
            width,
            lineHeight);

        GUI.Label(rect, label, distanceStyle);
        GUI.Label(forceRect, forceLabel, distanceStyle);
        GUI.Label(actionRect, actionLabel, distanceStyle);
    }
}
