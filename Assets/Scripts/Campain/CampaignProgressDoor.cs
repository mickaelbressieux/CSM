using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ouvre deux battants lorsque tous les adversaires demandes ont ete vaincus.
/// L'etat est lu depuis le CampainManager persistant et fonctionne sur toute map.
/// </summary>
[DisallowMultipleComponent]
public class CampaignProgressDoor : MonoBehaviour
{
    [Header("Battants")]
    [SerializeField] Transform leftDoor;
    [SerializeField] Transform rightDoor;
    [SerializeField] float openingAngle = 90f;
    [Min(0f)]
    [SerializeField] float openingSpeed = 120f;

    [Header("Victoires requises")]
    [Tooltip("La porte s'ouvre lorsque tous ces profils ont ete vaincus.")]
    [SerializeField] List<AIOpponentProfile> requiredOpponents = new List<AIOpponentProfile>();

    [Header("Limites de la camera apres ouverture")]
    [Tooltip("Modifie aussi la zone dans laquelle la camera peut se deplacer lorsque la porte s'ouvre.")]
    [SerializeField] bool changeCameraPositionLimits;
    [Tooltip("Camera concernee. Si elle est vide, la PlayerCamera active est recherchee automatiquement.")]
    [SerializeField] PlayerCamera playerCamera;
    [SerializeField] float cameraMinX = -50f;
    [SerializeField] float cameraMaxX = 50f;
    [SerializeField] float cameraMinZ = -50f;
    [SerializeField] float cameraMaxZ = 50f;

    Quaternion leftClosedRotation;
    Quaternion rightClosedRotation;
    bool openingRequested;
    bool cameraLimitsApplied;
    bool subscribed;

    void Awake()
    {
        if (leftDoor != null)
            leftClosedRotation = leftDoor.localRotation;
        if (rightDoor != null)
            rightClosedRotation = rightDoor.localRotation;
    }

    void OnEnable()
    {
        TrySubscribe();
        EvaluateRequirements();
    }

    void Start()
    {
        EvaluateRequirements();
    }

    void Update()
    {
        if (!subscribed)
        {
            TrySubscribe();
            EvaluateRequirements();
        }

        if (!openingRequested)
            return;

        ApplyCameraPositionLimits();

        float step = openingSpeed * Time.deltaTime;

        if (leftDoor != null)
        {
            Quaternion target = leftClosedRotation * Quaternion.Euler(0f, -openingAngle, 0f);
            leftDoor.localRotation = Quaternion.RotateTowards(leftDoor.localRotation, target, step);
        }

        if (rightDoor != null)
        {
            Quaternion target = rightClosedRotation * Quaternion.Euler(0f, openingAngle, 0f);
            rightDoor.localRotation = Quaternion.RotateTowards(rightDoor.localRotation, target, step);
        }
    }

    void TrySubscribe()
    {
        CampainManager manager = CampainManager.Instance;
        if (subscribed || manager == null)
            return;

        manager.OnProgressionChanged += EvaluateRequirements;
        subscribed = true;
    }

    void EvaluateRequirements()
    {
        CampainManager manager = CampainManager.Instance;
        openingRequested = manager != null && manager.AreOpponentsDefeated(requiredOpponents);

        if (openingRequested)
            ApplyCameraPositionLimits();
    }

    void ApplyCameraPositionLimits()
    {
        if (!changeCameraPositionLimits || cameraLimitsApplied)
            return;

        if (playerCamera == null)
            playerCamera = FindFirstObjectByType<PlayerCamera>();
        if (playerCamera == null)
            return;

        playerCamera.SetPositionLimits(cameraMinX, cameraMaxX, cameraMinZ, cameraMaxZ);
        cameraLimitsApplied = true;
    }

    void OnDisable()
    {
        CampainManager manager = CampainManager.Instance;
        if (subscribed && manager != null)
            manager.OnProgressionChanged -= EvaluateRequirements;

        subscribed = false;
    }

    void OnValidate()
    {
        openingSpeed = Mathf.Max(0f, openingSpeed);

        if (cameraMinX > cameraMaxX)
            (cameraMinX, cameraMaxX) = (cameraMaxX, cameraMinX);
        if (cameraMinZ > cameraMaxZ)
            (cameraMinZ, cameraMaxZ) = (cameraMaxZ, cameraMinZ);
    }
}
