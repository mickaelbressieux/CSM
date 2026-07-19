using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class AIStoneController : MonoBehaviour
{
    private enum StoneAction
    {
        TargetSimulation,
        ForceAction
    }

    [SerializeField] private string simulationTargetTag = "Center";
    [Header("UI")]
    [SerializeField] private bool showDistanceOnScreen = true;
    [SerializeField] private Vector2 uiMargin = new Vector2(16f, 16f);
    [SerializeField] private int fontSize = 20;

    [Header("Actions")]
    [SerializeField] private List<StoneAction> availableActions = new List<StoneAction>
    {
        StoneAction.TargetSimulation,
        StoneAction.ForceAction,
    };
    [SerializeField] private int selectedActionIndex;

    [Header("Obstacle Detection")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;
    [SerializeField] private float obstacleClearanceMargin = 0.15f;
    [SerializeField] private float stopRadiusAtCibleDistance = 0.75f;

    [Header("Target Simulation Action")]
    [SerializeField] private float simulationHitTolerance = 0.03f;
    [SerializeField] private float simulationFallbackDistance = 1f;
    [SerializeField] private float simulationForceMin = 10f;
    [SerializeField] private float simulationForceMax = 150f;
    [SerializeField] private float simulationAngleMin = -35f;
    [SerializeField] private float simulationAngleMax = 35f;
    [SerializeField] private float simulationAngleStep = 1f;
    [SerializeField] private float simulationCurlMin = -1.7f;
    [SerializeField] private float simulationCurlMax = 1.7f;
    [SerializeField] private float simulationCurlStep = 0.1f;

    [Header("Force Action")]
    [SerializeField] private float forceActionForce = 41.40f;
    [SerializeField] private float forceActionAngleDeg = 9.0f;
    [SerializeField] private float forceActionCurl = -0.30f;

    [Header("Physics Curl")]
    [SerializeField] private float slideDrag = 0.001f;
    [SerializeField] private float curlDegreesPerMeter = 0.4f;
    [SerializeField] private float preShotSpinSpeed = 2f;
    [SerializeField] private float stopThreshold = 0.05f;

    public float LastDistance { get; private set; } = -1f;
    public GameObject LastSimulationTarget { get; private set; }
    public float LastAppliedForce { get; private set; } = -1f;
    public int EnterActivationCount { get; private set; }

    // ── API externe (AISceneManager) ────────────────────────────────────────

    /// <summary>Tag de la cible utilisé par la simulation (lecture/écriture).</summary>
    public string SimulationTargetTag
    {
        get => simulationTargetTag;
        set => simulationTargetTag = value;
    }

    /// <summary>
    /// Calcule la trajectoire vers la cible (tag SimulationTargetTag) et place
    /// les paramètres en attente sans les modifier.
    /// Retourne true si un tir a été mis en file, false sinon.
    /// </summary>
    public bool PrepareSimulationLaunch()
    {
        launchCiblePending = false;
        cibleStopActive = false;
        stoneIsFlying = false;
        if (rb != null) rb.linearDamping = 0f;
        LaunchTowardSimulationTarget();
        return launchCiblePending;
    }

    /// <summary>
    /// Calcule la trajectoire vers une cible explicite et place les paramètres
    /// en attente sans les modifier.
    /// Retourne true si un tir a été mis en file, false sinon.
    /// </summary>
    public bool PrepareSimulationLaunchToTarget(GameObject target)
    {
        launchCiblePending = false;
        cibleStopActive = false;
        stoneIsFlying = false;
        if (rb != null) rb.linearDamping = 0f;
        LaunchTowardResolvedTarget(target);
        return launchCiblePending;
    }

    /// <summary>
    /// Retourne les paramètres du tir actuellement en attente.
    /// Retourne false si aucun tir n'est en file.
    /// </summary>
    public bool GetPendingLaunchParams(out float force, out float angleDeg, out float curl)
    {
        force    = pendingForce;
        angleDeg = pendingAngleOffsetDeg;
        curl     = pendingCurl;
        return launchCiblePending;
    }

    /// <summary>
    /// Remplace les paramètres du tir en attente (bruit/bonus appliqués par AISceneManager).
    /// N'a d'effet que si un tir est effectivement en file (launchCiblePending == true).
    /// </summary>
    public void OverridePendingLaunchParams(float force, float angleDeg, float curl)
    {
        if (!launchCiblePending) return;
        pendingForce          = Mathf.Max(0.01f, force);
        pendingAngleOffsetDeg = angleDeg;
        pendingCurl           = curl;
        pendingAngVelY        = curl * preShotSpinSpeed;
        if (rb != null)
            pendingSpeed = Mathf.Max(0.001f, pendingForce / Mathf.Max(0.001f, rb.mass));
    }

    private GUIStyle distanceStyle;
    private bool warnedMissingSimulationTag;
    private Rigidbody rb;

    private Collider[] ownColliders;
    private bool cibleStopActive;
    private Vector3 cibleStopWorldPosition;
    private float activeCurl = 0f;
    private float launchAngVelY = 0f;
    private float launchSpeed = 1f;
    private bool stoneIsFlying = false;

    // Tir en attente : l'impulsion est appliquee dans FixedUpdate (comme CurlingStoneController)
    private bool launchCiblePending = false;
    private Vector3 pendingDir;          // direction de base (vers la cible, sans angle)
    private float pendingAngleOffsetDeg; // angle a appliquer au lancement (comme CurlingStoneController.aimAngle)
    private float pendingForce;
    private float pendingCurl;
    private float pendingAngVelY;
    private float pendingSpeed;
    private Vector3 pendingStopPos;
    private bool pendingStopActive;

    private readonly Dictionary<Vector2Int, Vector2> predictorStopByKey = new Dictionary<Vector2Int, Vector2>();
    private readonly Dictionary<Vector2Int, Vector2[]> predictorWaypointsByKey = new Dictionary<Vector2Int, Vector2[]>();
    private readonly List<float> predictorForceGrid = new List<float>();
    private readonly List<float> predictorCurlGrid = new List<float>();
    private bool predictorLoaded;
    private const int   PredictorWaypointCount     = 20;
    // Rayon maximal estimé des obstacles présents sur le terrain (joueurs ~0.3 m, pierres ~0.15 m).
    // Utilisé pour dimensionner le scan initial avant vérification de la formule exacte.
    private const float kMaxExpectedObstacleRadius = 0.5f;
    private static readonly Regex PredictorFileRegex =
        new Regex(@"^sim_F(?<f>[0-9]+(?:,[0-9]+)?)_C(?<neg>m)?(?<c>[0-9]+(?:,[0-9]+)?)\.txt$", RegexOptions.Compiled);

    private void Awake()
    {
        distanceStyle = new GUIStyle();
        distanceStyle.alignment = TextAnchor.UpperRight;
        distanceStyle.fontSize = fontSize;
        distanceStyle.normal.textColor = Color.white;

        rb = GetComponent<Rigidbody>();
        ownColliders = GetComponentsInChildren<Collider>();
        EnsureAvailableActionsConfigured();
        ClampSelectedActionIndex();
    }

    private void OnValidate()
    {
        EnsureAvailableActionsConfigured();
        ClampSelectedActionIndex();

        stopRadiusAtCibleDistance = Mathf.Max(0.05f, stopRadiusAtCibleDistance);
        obstacleClearanceMargin = Mathf.Max(0f, obstacleClearanceMargin);
        simulationHitTolerance = Mathf.Max(0.001f, simulationHitTolerance);
        simulationFallbackDistance = Mathf.Max(simulationHitTolerance, simulationFallbackDistance);
        simulationForceMin = Mathf.Max(0.01f, simulationForceMin);
        simulationForceMax = Mathf.Max(simulationForceMin, simulationForceMax);
        simulationAngleStep = Mathf.Max(0.1f, simulationAngleStep);
        simulationCurlStep = Mathf.Max(0.01f, simulationCurlStep);
    }

    private void Update()
    {
        HandleActionSelectionInput();

        if (!WasEnterPressedThisFrame())
            return;

        EnterActivationCount++;
        ExecuteSelectedAction();
    }

    private void FixedUpdate()
    {
        // Appliquer le tir en attente (comme CurlingStoneController : impulsion dans FixedUpdate, return immediat)
        if (launchCiblePending && rb != null)
        {
            launchCiblePending = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.linearDamping = slideDrag;
            // Appliquer l'angle comme CurlingStoneController : Quaternion.Euler(0, aimAngle, 0) * forward
            Vector3 launchDirFinal = Quaternion.Euler(0f, pendingAngleOffsetDeg, 0f) * pendingDir;
            float launchYawDeg = Mathf.Atan2(launchDirFinal.x, launchDirFinal.z) * Mathf.Rad2Deg;
            Debug.Log("[Launch Force] Direction=" + launchDirFinal.normalized.ToString("F3")
                + " | Yaw=" + launchYawDeg.ToString("F1") + "deg"
                + " | Force=" + pendingForce.ToString("F2")
                + " | Curl=" + pendingCurl.ToString("F2"), this);
            rb.AddForce(launchDirFinal * pendingForce, ForceMode.Impulse);
            activeCurl = pendingCurl;
            launchAngVelY = pendingAngVelY;
            launchSpeed = pendingSpeed;
            rb.angularVelocity = new Vector3(0f, launchAngVelY, 0f);
            stoneIsFlying = true;
            cibleStopWorldPosition = pendingStopPos;
            cibleStopActive = pendingStopActive;
            return;
        }

        if (stoneIsFlying && rb != null)
        {
            if (rb.linearVelocity.magnitude <= stopThreshold)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.linearDamping = 0f;
                rb.constraints = RigidbodyConstraints.None;
                stoneIsFlying = false;
                cibleStopActive = false;
                return;
            }

            // Decroissance du spin proportionnellement a la vitesse (comme CurlingStoneController)
            float speedRatio = (launchSpeed > 0.001f) ? rb.linearVelocity.magnitude / launchSpeed : 0f;
            rb.angularVelocity = new Vector3(0f, launchAngVelY * speedRatio, 0f);

            // Curl : rotation du vecteur vitesse a chaque pas de physique
            if (Mathf.Abs(activeCurl) > 0.001f)
            {
                float speed = rb.linearVelocity.magnitude;
                float distThisStep = speed * Time.fixedDeltaTime;
                float angleDeg = activeCurl * curlDegreesPerMeter * distThisStep;
                rb.linearVelocity = Quaternion.Euler(0f, angleDeg, 0f) * rb.linearVelocity;
            }
        }

        if (!cibleStopActive || rb == null)
            return;

        Vector3 currentXZ = rb.position;
        currentXZ.y = 0f;
        Vector3 stopXZ = cibleStopWorldPosition;
        stopXZ.y = 0f;

        float dist = Vector3.Distance(currentXZ, stopXZ);
        if (dist <= stopRadiusAtCibleDistance)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearDamping = 0f;
            rb.constraints = RigidbodyConstraints.None;
            stoneIsFlying = false;
            cibleStopActive = false;
        }
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
            case StoneAction.TargetSimulation:
                launchCiblePending = false;
                cibleStopActive = false;
                stoneIsFlying = false;
                if (rb != null) rb.linearDamping = 0f;
                LaunchTowardSimulationTarget();
                break;
            case StoneAction.ForceAction:
                launchCiblePending = false;
                cibleStopActive = false;
                stoneIsFlying = false;
                if (rb != null) rb.linearDamping = 0f;
                LaunchForceAction();
                break;
        }
    }

    private void LaunchForceAction()
    {
        if (rb == null)
        {
            Debug.LogWarning("ForceAction: Rigidbody manquant.", this);
            return;
        }

        // Direction de base : vers l'avant du transform
        Vector3 baseDir = transform.forward;
        baseDir.y = 0f;
        if (baseDir.sqrMagnitude < 0.0001f)
            baseDir = Vector3.forward;
        else
            baseDir.Normalize();

        float baseYaw = Mathf.Atan2(baseDir.x, baseDir.z);
        float yawWithAngle = baseYaw + forceActionAngleDeg * Mathf.Deg2Rad;

        pendingDir = baseDir;
        pendingAngleOffsetDeg = forceActionAngleDeg;
        pendingForce = forceActionForce;
        pendingCurl = forceActionCurl;
        pendingAngVelY = forceActionCurl * preShotSpinSpeed;
        pendingSpeed = Mathf.Max(0.001f, forceActionForce / Mathf.Max(0.001f, rb.mass));
        pendingStopPos = transform.position;
        pendingStopActive = false;
        launchCiblePending = true;

        Debug.Log("[ForceAction] Force=" + forceActionForce.ToString("F2")
            + " | Angle=" + forceActionAngleDeg.ToString("F1") + "deg"
            + " | Curl=" + forceActionCurl.ToString("F2"), this);
    }

    private void LaunchTowardSimulationTarget()
    {
        if (rb == null)
        {
            Debug.LogWarning("Rigidbody manquant: simulation annulee.", this);
            return;
        }

        if (!TryFindNearestWithTag(simulationTargetTag, gameObject, out GameObject target, out float targetDistance))
        {
            LastSimulationTarget = null;
            LastDistance = -1f;
            return;
        }

        LaunchTowardResolvedTarget(target);
    }

    private void LaunchTowardResolvedTarget(GameObject target)
    {
        if (rb == null)
        {
            Debug.LogWarning("Rigidbody manquant: simulation annulee.", this);
            return;
        }

        if (target == null)
        {
            LastSimulationTarget = null;
            LastDistance = -1f;
            Debug.LogWarning("TargetSimulation: cible explicite invalide (null).", this);
            return;
        }

        Vector3 flatToTarget = target.transform.position - transform.position;
        flatToTarget.y = 0f;
        float targetDistance = flatToTarget.magnitude;

        LastSimulationTarget = target;
        LastDistance = targetDistance;

        if (targetDistance <= 0.01f)
        {
            Debug.LogWarning("TargetSimulation: cible trop proche ou meme position.", this);
            return;
        }

        if (!EnsurePredictorLoaded())
        {
            Debug.LogWarning("TargetSimulation: predictor non charge, simulation impossible.", this);
            return;
        }

        Vector3 baseDir = flatToTarget.normalized;
        float baseYaw = Mathf.Atan2(baseDir.x, baseDir.z);
        Vector3 targetXZ = new Vector3(target.transform.position.x, 0f, target.transform.position.z);

        if (!TryFindBinaryForceToTarget(baseYaw, targetXZ, out float f1, out float f1Error))
        {
            Debug.LogWarning("TargetSimulation: impossible de trouver F1 via dichotomie.", this);
            return;
        }
        Debug.Log("[TargetSimulation F1] ForceF1=" + f1.ToString("F2")
            + " | Angle=0.0deg | Curl=0.00 | DistClosest=" + f1Error.ToString("F3") + "m", this);

        bool directBlocked = IsTrajectoryObstructedIgnoringGround(transform.position, target.transform.position, target);
        if (!directBlocked)
        {
            if (f1Error > simulationFallbackDistance)
            {
                QueueSimulationShot(baseDir, 0f, f1, 0f, target.transform.position, false);
                LastAppliedForce = f1;
                Debug.LogWarning("[TargetSimulation F1 Only] Trajectoire directe hors fallback, application F1 seule. Force=" + f1.ToString("F2")
                    + " | Angle=0.0deg | Curl=0.0 | Error=" + f1Error.ToString("F3") + "m", this);
                return;
            }

            QueueSimulationShot(baseDir, 0f, f1, 0f, target.transform.position, false);
            LastAppliedForce = f1;
            Debug.Log("[TargetSimulation Direct] Force=" + f1.ToString("F2")
                + " | Angle=0.0deg | Curl=0.0 | Error=" + f1Error.ToString("F3") + "m", this);
            return;
        }

        if (!TrySearchSimulationWithObstacles(baseYaw, targetXZ, target, f1,
                out float bestForce, out float bestAngleDeg, out float bestCurl, out float bestError, out Vector3 bestStop))
        {
            QueueSimulationShot(baseDir, 0f, f1, 0f, target.transform.position, false);
            LastAppliedForce = f1;
            Debug.LogWarning("[TargetSimulation F1 Only] Aucune trajectoire valide avec obstacles, application F1 seule. Force=" + f1.ToString("F2")
                + " | Angle=0.0deg | Curl=0.0", this);
            return;
        }

        QueueSimulationShot(baseDir, bestAngleDeg, bestForce, -bestCurl, bestStop, false);
        LastAppliedForce = bestForce;

        if (bestError <= simulationHitTolerance)
        {
            Debug.Log("[TargetSimulation] Force=" + bestForce.ToString("F2")
                + " | Angle=" + bestAngleDeg.ToString("F1")
                + "deg | Curl=" + bestCurl.ToString("F2")
                + " | Error=" + bestError.ToString("F3") + "m", this);
        }
        else
        {
            Debug.LogWarning("[TargetSimulation Fallback] Force=" + bestForce.ToString("F2")
                + " | Angle=" + bestAngleDeg.ToString("F1")
                + "deg | Curl=" + bestCurl.ToString("F2")
                + " | Error=" + bestError.ToString("F3") + "m (fallback < " + simulationFallbackDistance.ToString("F2") + "m)", this);
        }
    }

    private void QueueSimulationShot(Vector3 baseDirection, float angleDeg, float force, float curl, Vector3 stopPos, bool stopActive)
    {
        pendingDir = baseDirection;
        pendingAngleOffsetDeg = angleDeg;
        pendingForce = force;
        pendingCurl = curl;
        pendingAngVelY = curl * preShotSpinSpeed;
        pendingSpeed = Mathf.Max(0.001f, force / Mathf.Max(0.001f, rb.mass));
        pendingStopPos = stopPos;
        pendingStopActive = stopActive;
        launchCiblePending = true;
    }

    private bool TryFindBinaryForceToTarget(float baseYaw, Vector3 targetXZ, out float bestForce, out float bestError)
    {
        bestForce = 0f;
        bestError = float.MaxValue;

        float low = simulationForceMin;
        float high = simulationForceMax;

        for (int i = 0; i < 24; i++)
        {
            float mid = (low + high) * 0.5f;
            if (!TryPredictWorldStop(mid, 0f, 0f, baseYaw, out Vector3 stopMid))
                return false;

            float distError = Vector3.Distance(new Vector3(stopMid.x, 0f, stopMid.z), targetXZ);
            if (distError < bestError)
            {
                bestError = distError;
                bestForce = mid;
            }

            Vector3 delta = stopMid - targetXZ;
            delta.y = 0f;
            Vector3 forward = new Vector3(Mathf.Sin(baseYaw), 0f, Mathf.Cos(baseYaw));
            float signedAlong = Vector3.Dot(delta, forward);

            if (signedAlong > 0f)
                high = mid;
            else
                low = mid;
        }

        return bestForce > 0f;
    }

    private bool TrySearchSimulationWithObstacles(
        float baseYaw,
        Vector3 targetXZ,
        GameObject excludeTarget,
        float startForce,
        out float bestForce,
        out float bestAngleDeg,
        out float bestCurl,
        out float bestError,
        out Vector3 bestStop)
    {
        bestForce = 0f;
        bestAngleDeg = 0f;
        bestCurl = 0f;
        bestError = float.MaxValue;
        bestStop = transform.position;

        float forceStart = startForce * 0.9f;
        float forceStep  = Mathf.Max(0.01f, startForce / 100f);
        float forceEnd   = startForce * 2f;

        int forceCount = Mathf.Max(1, Mathf.RoundToInt((forceEnd - forceStart) / forceStep)) + 1;
        int angleCount = Mathf.Max(1, Mathf.RoundToInt((simulationAngleMax - simulationAngleMin) / simulationAngleStep)) + 1;
        int curlCount  = Mathf.Max(1, Mathf.RoundToInt((simulationCurlMax  - simulationCurlMin)  / simulationCurlStep))  + 1;
        int total = forceCount * angleCount * curlCount;

        // Capturer les valeurs Unity avant de quitter le thread principal
        Vector3 myPos    = transform.position;
        float fallback   = simulationFallbackDistance;
        float angMin     = simulationAngleMin;
        float curlMinVal = simulationCurlMin;
        float angStep    = simulationAngleStep;
        float cStep      = simulationCurlStep;

        // Passe 1 : géométrie pure en parallèle (lookups de table + maths uniquement, pas d'API Unity physique)
        var candidates = new System.Collections.Concurrent.ConcurrentBag<(float error, float force, float angle, float curl, Vector3 stop)>();

        System.Threading.Tasks.Parallel.For(0, total, idx =>
        {
            int fi = idx / (angleCount * curlCount);
            int ai = (idx / curlCount) % angleCount;
            int ci = idx % curlCount;

            float force = forceStart + fi * forceStep;
            float angle = angMin     + ai * angStep;
            float curl  = curlMinVal + ci * cStep;

            if (!TryPredictFinalOffset(force, curl, out Vector2 localStop))
                return;

            float yaw    = baseYaw + (angle * Mathf.Deg2Rad);
            float cosYaw = Mathf.Cos(yaw);
            float sinYaw = Mathf.Sin(yaw);
            float wx = localStop.x * cosYaw + localStop.y * sinYaw;
            float wz = -localStop.x * sinYaw + localStop.y * cosYaw;
            Vector3 stopWorld = new Vector3(myPos.x + wx, myPos.y, myPos.z + wz);

            float hitError = Vector3.Distance(new Vector3(stopWorld.x, 0f, stopWorld.z), targetXZ);
            if (hitError <= fallback)
                candidates.Add((hitError, force, angle, curl, stopWorld));
        });

        if (candidates.IsEmpty)
            return false;

        // Tri par erreur croissante
        var sorted = new List<(float error, float force, float angle, float curl, Vector3 stop)>(candidates);
        sorted.Sort((a, b) => a.error.CompareTo(b.error));

        // Passe 2 : check obstacles sur le thread principal (API Unity physique), du meilleur au pire
        foreach (var (error, force, angle, curl, stop) in sorted)
        {
            float yaw = baseYaw + (angle * Mathf.Deg2Rad);
            bool blocked = Mathf.Abs(curl) > 0.001f
                ? IsCurvedPathObstructedIgnoringGround(transform.position, force, curl, yaw, excludeTarget)
                : IsTrajectoryObstructedIgnoringGround(transform.position, stop, excludeTarget);
            if (blocked)
                continue;

            bestError    = error;
            bestForce    = force;
            bestAngleDeg = angle;
            bestCurl     = curl;
            bestStop     = stop;

            Debug.Log("[TargetSimulation Search Best] DistClosest=" + bestError.ToString("F3")
                + "m | Force=" + bestForce.ToString("F2")
                + " | Angle=" + bestAngleDeg.ToString("F1")
                + "deg | Curl=" + bestCurl.ToString("F2")
                + " | Accepted=" + (bestError <= simulationFallbackDistance), this);
            return bestError <= simulationFallbackDistance;
        }

        return false;
    }

    private bool TryPredictWorldStop(float force, float angleDeg, float curl, float baseYaw, out Vector3 stopWorld)
    {
        stopWorld = transform.position;
        if (!TryPredictFinalOffset(force, curl, out Vector2 localStop))
            return false;

        float yaw = baseYaw + (angleDeg * Mathf.Deg2Rad);
        float cosYaw = Mathf.Cos(yaw);
        float sinYaw = Mathf.Sin(yaw);

        float wx = localStop.x * cosYaw + localStop.y * sinYaw;
        float wz = -localStop.x * sinYaw + localStop.y * cosYaw;
        stopWorld = new Vector3(transform.position.x + wx, transform.position.y, transform.position.z + wz);
        return true;
    }

    private bool IsTrajectoryObstructedIgnoringGround(Vector3 from, Vector3 to, GameObject excludeObject = null)
    {
        Vector3 dir = to - from;
        dir.y = 0f;
        if (dir.magnitude < 0.01f)
            return false;

        float stoneRadius = GetStoneRadius();
        float capsuleRadius = stoneRadius + obstacleClearanceMargin;
        Collider[] hits = Physics.OverlapCapsule(from, to, capsuleRadius,
            obstacleLayerMask, QueryTriggerInteraction.Ignore);

        foreach (Collider hit in hits)
        {
            if (IsColliderBlockingSimulation(hit, from.y, excludeObject, true))
                return true;
        }

        return false;
    }

    private bool IsCurvedPathObstructedIgnoringGround(Vector3 from, float force, float curl, float yaw, GameObject excludeObject)
    {
        if (!TryPredictWaypoints(force, curl, out Vector2[] waypoints))
            return false;

        float stoneRadius = GetStoneRadius();
        float cosYaw = Mathf.Cos(yaw);
        float sinYaw = Mathf.Sin(yaw);
        float scanRadius = stoneRadius + obstacleClearanceMargin + kMaxExpectedObstacleRadius;

        Vector3 prev = from;
        foreach (Vector2 wp in waypoints)
        {
            float wx = wp.x * cosYaw + wp.y * sinYaw;
            float wz = -wp.x * sinYaw + wp.y * cosYaw;
            Vector3 point = new Vector3(from.x + wx, from.y, from.z + wz);

            Collider[] hits = Physics.OverlapCapsule(prev, point, scanRadius,
                obstacleLayerMask, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (!IsColliderBlockingSimulation(hit, from.y, excludeObject, true))
                    continue;

                float obstacleRadius = GetColliderMaxXZRadius(hit);
                float requiredClearance = stoneRadius + obstacleRadius + obstacleClearanceMargin;
                float dist = SegmentPointDistance2D(prev, point, hit.bounds.center);
                if (dist < requiredClearance)
                    return true;
            }
            prev = point;
        }

        return false;
    }

    private bool IsColliderBlockingSimulation(Collider hit, float fromY, GameObject excludeObject, bool ignoreGroundLayer)
    {
        if (hit == null)
            return false;
        if (excludeObject != null && hit.transform.IsChildOf(excludeObject.transform))
            return false;
        if (hit.bounds.max.y <= fromY + 0.05f)
            return false;
        if (ownColliders != null)
        {
            foreach (Collider sc in ownColliders)
            {
                if (hit == sc)
                    return false;
            }
        }

        if (ignoreGroundLayer)
        {
            string layerName = LayerMask.LayerToName(hit.gameObject.layer);
            if (!string.IsNullOrEmpty(layerName)
                && string.Equals(layerName, "ground", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private bool TryFindNearestWithTag(string tagName, GameObject excludeObject, out GameObject nearest, out float nearestDistance)
    {
        nearest = null;
        nearestDistance = -1f;

        if (!IsTagDefined(tagName))
        {
            if (!warnedMissingSimulationTag)
            {
                warnedMissingSimulationTag = true;
                Debug.LogWarning("Le tag de simulation '" + tagName + "' n'existe pas dans Tags and Layers.", this);
            }
            return false;
        }

        warnedMissingSimulationTag = false;
        GameObject[] candidates = GameObject.FindGameObjectsWithTag(tagName);
        if (candidates == null || candidates.Length == 0)
        {
            Debug.LogWarning("Aucun objet avec le tag de simulation '" + tagName + "' n'a ete trouve.", this);
            return false;
        }

        Vector3 myPos = transform.position;
        float bestSqr = float.MaxValue;
        foreach (GameObject go in candidates)
        {
            if (go == null)
                continue;
            if (excludeObject != null && go == excludeObject)
                continue;

            Vector3 d = go.transform.position - myPos;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                nearest = go;
            }
        }

        if (nearest == null)
            return false;

        nearestDistance = Mathf.Sqrt(bestSqr);
        return true;
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



    private float GetStoneRadius()
    {
        if (ownColliders == null || ownColliders.Length == 0)
            return 0.15f;

        float r = 0f;
        foreach (Collider c in ownColliders)
            r = Mathf.Max(r, GetColliderMaxXZRadius(c));
        return r > 0f ? r : 0.15f;
    }

    private static float GetColliderMaxXZRadius(Collider c)
    {
        if (c == null) return 0f;
        if (c is MeshCollider mc && mc.sharedMesh != null)
        {
            if (!mc.sharedMesh.isReadable)
                return Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);

            Vector3 center = c.bounds.center;
            float maxR = 0f;
            foreach (Vector3 v in mc.sharedMesh.vertices)
            {
                Vector3 world = c.transform.TransformPoint(v);
                float dx = world.x - center.x;
                float dz = world.z - center.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r > maxR) maxR = r;
            }
            return maxR;
        }
        if (c is SphereCollider sc)
        {
            Vector3 s = c.transform.lossyScale;
            return sc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }
        if (c is CapsuleCollider cc)
        {
            Vector3 s = c.transform.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }
        return Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);
    }


    // Verifie une trajectoire courbe (curl) en echantillonnant l'arc en N segments.
    // La derive laterale suit une loi quadratique : lat(t) = localStop.x * t^2
    // (consequence d'une courbure constante : derive ~ arc^2 / 2R pour petits angles)
    // Prédit les waypoints (N=PredictorWaypointCount) par interpolation bilinéaire de la table.
    private bool TryPredictWaypoints(float force, float curl, out Vector2[] waypoints)
    {
        waypoints = null;
        if (predictorForceGrid.Count == 0 || predictorCurlGrid.Count == 0)
            return false;

        float f = Mathf.Clamp(force, predictorForceGrid[0], predictorForceGrid[predictorForceGrid.Count - 1]);
        float c = Mathf.Clamp(curl,  predictorCurlGrid[0],  predictorCurlGrid[predictorCurlGrid.Count  - 1]);

        int i0 = FindLowerIndex(predictorForceGrid, f);
        int i1 = Mathf.Min(i0 + 1, predictorForceGrid.Count - 1);
        int j0 = FindLowerIndex(predictorCurlGrid,  c);
        int j1 = Mathf.Min(j0 + 1, predictorCurlGrid.Count  - 1);

        float f0 = predictorForceGrid[i0], f1 = predictorForceGrid[i1];
        float c0 = predictorCurlGrid[j0],  c1 = predictorCurlGrid[j1];

        Vector2Int k00 = PredictorKey(f0, c0);
        Vector2Int k10 = PredictorKey(f1, c0);
        Vector2Int k01 = PredictorKey(f0, c1);
        Vector2Int k11 = PredictorKey(f1, c1);

        if (!predictorWaypointsByKey.TryGetValue(k00, out Vector2[] w00)) return false;
        if (!predictorWaypointsByKey.TryGetValue(k10, out Vector2[] w10)) w10 = w00;
        if (!predictorWaypointsByKey.TryGetValue(k01, out Vector2[] w01)) w01 = w00;
        if (!predictorWaypointsByKey.TryGetValue(k11, out Vector2[] w11)) w11 = w00;

        float tf = Mathf.Approximately(f0, f1) ? 0f : (f - f0) / (f1 - f0);
        float tc = Mathf.Approximately(c0, c1) ? 0f : (c - c0) / (c1 - c0);

        int n = w00.Length;
        waypoints = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 a = Vector2.Lerp(w00[i], w10[i], tf);
            Vector2 b = Vector2.Lerp(w01[i], w11[i], tf);
            waypoints[i] = Vector2.Lerp(a, b, tc);
        }
        return true;
    }


    // Distance 2D (plan XZ) entre un point P et le segment [A, B].
    private static float SegmentPointDistance2D(Vector3 a, Vector3 b, Vector3 p)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float lenSq = dx * dx + dz * dz;
        if (lenSq < 1e-10f)
        {
            float ex = p.x - a.x, ez = p.z - a.z;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }
        float t = Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / lenSq);
        float cx = a.x + t * dx;
        float cz = a.z + t * dz;
        float fx = p.x - cx, fz = p.z - cz;
        return Mathf.Sqrt(fx * fx + fz * fz);
    }

    private bool EnsurePredictorLoaded()
    {
        if (predictorLoaded)
            return predictorForceGrid.Count > 0 && predictorCurlGrid.Count > 0;

        predictorLoaded = true;
        predictorStopByKey.Clear();
        predictorWaypointsByKey.Clear();
        predictorForceGrid.Clear();
        predictorCurlGrid.Clear();

        string logsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "simu curl pc"));
        if (!Directory.Exists(logsPath))
        {
            Debug.LogWarning("Dossier de logs introuvable: " + logsPath, this);
            return false;
        }

        var accum      = new Dictionary<Vector2Int, List<Vector2>>();
        var accumWp    = new Dictionary<Vector2Int, List<Vector2[]>>();
        foreach (string path in Directory.EnumerateFiles(logsPath, "sim_F*_C*.txt"))
        {
            string fileName = Path.GetFileName(path);
            if (!TryParseForceCurlFromName(fileName, out float force, out float curl))
                continue;

            if (!TryReadNormalizedWaypoints(path, PredictorWaypointCount, out Vector2[] wpts))
                continue;

            Vector2 stopPos = wpts[PredictorWaypointCount - 1]; // dernier waypoint = point final
            Vector2Int key  = PredictorKey(force, curl);

            if (!accum.TryGetValue(key, out List<Vector2> list))
            {
                list = new List<Vector2>();
                accum.Add(key, list);
            }
            list.Add(stopPos);

            if (!accumWp.TryGetValue(key, out List<Vector2[]> wlist))
            {
                wlist = new List<Vector2[]>();
                accumWp.Add(key, wlist);
            }
            wlist.Add(wpts);
        }

        HashSet<float> forceSet = new HashSet<float>();
        HashSet<float> curlSet  = new HashSet<float>();

        foreach (KeyValuePair<Vector2Int, List<Vector2>> kv in accum)
        {
            Vector2 mean = Vector2.zero;
            foreach (Vector2 p in kv.Value)
                mean += p;
            mean /= Mathf.Max(1, kv.Value.Count);

            predictorStopByKey[kv.Key] = mean;
            forceSet.Add(kv.Key.x / 100f);
            curlSet.Add(kv.Key.y / 100f);
        }

        // Calcul des waypoints moyens
        foreach (KeyValuePair<Vector2Int, List<Vector2[]>> kv in accumWp)
        {
            Vector2[] meanWp = new Vector2[PredictorWaypointCount];
            foreach (Vector2[] wp in kv.Value)
                for (int i = 0; i < PredictorWaypointCount; i++)
                    meanWp[i] += wp[i];
            float inv = 1f / Mathf.Max(1, kv.Value.Count);
            for (int i = 0; i < PredictorWaypointCount; i++)
                meanWp[i] *= inv;
            predictorWaypointsByKey[kv.Key] = meanWp;
        }

        predictorForceGrid.AddRange(forceSet);
        predictorCurlGrid.AddRange(curlSet);
        predictorForceGrid.Sort();
        predictorCurlGrid.Sort();

        bool loaded = predictorForceGrid.Count > 0 && predictorCurlGrid.Count > 0;
        if (loaded)
            Debug.Log("Predictor charge: " + predictorStopByKey.Count + " entrees (" + PredictorWaypointCount + " waypoints), forces [" + predictorForceGrid[0].ToString("F2") + " - " + predictorForceGrid[predictorForceGrid.Count - 1].ToString("F2") + "], curls [" + predictorCurlGrid[0].ToString("F2") + " - " + predictorCurlGrid[predictorCurlGrid.Count - 1].ToString("F2") + "]", this);
        else
            Debug.LogWarning("Predictor: aucune entree chargee depuis " + logsPath, this);
        return loaded;
    }

    private bool TryPredictFinalOffset(float force, float curl, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (predictorForceGrid.Count == 0 || predictorCurlGrid.Count == 0)
            return false;

        float f = Mathf.Clamp(force, predictorForceGrid[0], predictorForceGrid[predictorForceGrid.Count - 1]);
        float c = Mathf.Clamp(curl, predictorCurlGrid[0], predictorCurlGrid[predictorCurlGrid.Count - 1]);

        int i0 = FindLowerIndex(predictorForceGrid, f);
        int i1 = Mathf.Min(i0 + 1, predictorForceGrid.Count - 1);
        int j0 = FindLowerIndex(predictorCurlGrid, c);
        int j1 = Mathf.Min(j0 + 1, predictorCurlGrid.Count - 1);

        float f0 = predictorForceGrid[i0];
        float f1 = predictorForceGrid[i1];
        float c0 = predictorCurlGrid[j0];
        float c1 = predictorCurlGrid[j1];

        Vector2 q00 = GetNearestKnownOffset(f0, c0);
        Vector2 q10 = GetNearestKnownOffset(f1, c0);
        Vector2 q01 = GetNearestKnownOffset(f0, c1);
        Vector2 q11 = GetNearestKnownOffset(f1, c1);

        float tf = Mathf.Approximately(f0, f1) ? 0f : (f - f0) / (f1 - f0);
        float tc = Mathf.Approximately(c0, c1) ? 0f : (c - c0) / (c1 - c0);

        Vector2 a = Vector2.Lerp(q00, q10, tf);
        Vector2 b = Vector2.Lerp(q01, q11, tf);
        offset = Vector2.Lerp(a, b, tc);
        return true;
    }

    private static Vector2Int PredictorKey(float force, float curl)
    {
        return new Vector2Int(Mathf.RoundToInt(force * 100f), Mathf.RoundToInt(curl * 100f));
    }

    private Vector2 GetNearestKnownOffset(float force, float curl)
    {
        Vector2Int key = PredictorKey(force, curl);
        if (predictorStopByKey.TryGetValue(key, out Vector2 exact))
            return exact;

        float bestD2 = float.MaxValue;
        Vector2 best = Vector2.zero;
        foreach (KeyValuePair<Vector2Int, Vector2> kv in predictorStopByKey)
        {
            float df = (kv.Key.x / 100f) - force;
            float dc = (kv.Key.y / 100f) - curl;
            float d2 = (df * df) + (dc * dc);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = kv.Value;
            }
        }

        return best;
    }

    private static int FindLowerIndex(List<float> grid, float value)
    {
        if (value <= grid[0])
            return 0;

        for (int i = 0; i < grid.Count - 1; i++)
        {
            if (grid[i] <= value && value <= grid[i + 1])
                return i;
        }

        return Mathf.Max(0, grid.Count - 2);
    }

    private static bool TryParseForceCurlFromName(string fileName, out float force, out float curl)
    {
        force = 0f;
        curl = 0f;

        Match m = PredictorFileRegex.Match(fileName);
        if (!m.Success)
            return false;

        if (!TryParseFrNumber(m.Groups["f"].Value, out force))
            return false;
        if (!TryParseFrNumber(m.Groups["c"].Value, out curl))
            return false;

        if (m.Groups["neg"].Success)
            curl = -curl;

        return true;
    }

    // Lit un fichier de log et extrait N waypoints normalisés à τ = k/N (k=1..N).
    // Le dernier waypoint (k=N, τ=1) correspond au point d'arrêt final.
    private static bool TryReadNormalizedWaypoints(string filePath, int n, out Vector2[] waypoints)
    {
        waypoints = null;
        var pts = new List<Vector3>(); // (time, x, z) stockés dans x/y/z du Vector3

        foreach (string raw in File.ReadLines(filePath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!TryParseFrNumber(parts[0], out float t)) continue;
            if (!TryParseFrNumber(parts[1], out float x)) continue;
            if (!TryParseFrNumber(parts[2], out float z)) continue;
            pts.Add(new Vector3(t, x, z));
        }

        if (pts.Count < 2) return false;

        float tFinal = pts[pts.Count - 1].x;
        if (tFinal <= 0f) return false;

        waypoints = new Vector2[n];
        for (int k = 1; k <= n; k++)
        {
            float tau    = (float)k / n;
            float target = tau * tFinal;

            int lo = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (pts[i].x <= target && pts[i + 1].x >= target)
                {
                    lo = i;
                    break;
                }
                lo = i;
            }
            int hi = Mathf.Min(lo + 1, pts.Count - 1);

            float t0 = pts[lo].x, t1 = pts[hi].x;
            float alpha = Mathf.Approximately(t0, t1) ? 0f : (target - t0) / (t1 - t0);
            waypoints[k - 1] = new Vector2(
                Mathf.Lerp(pts[lo].y, pts[hi].y, alpha),
                Mathf.Lerp(pts[lo].z, pts[hi].z, alpha));
        }
        return true;
    }

    private static bool TryParseFrNumber(string s, out float value)
    {
        s = s.Replace(',', '.');
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

        if (!availableActions.Contains(StoneAction.TargetSimulation))
            availableActions.Add(StoneAction.TargetSimulation);

        if (!availableActions.Contains(StoneAction.ForceAction))
            availableActions.Add(StoneAction.ForceAction);
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
