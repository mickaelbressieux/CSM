using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class PlayerMotionCampagne : MonoBehaviour
{
    [Header("Groupe du joueur")]
    [Tooltip("Racine commune de tous les objets qui representent le joueur. Si elle est vide, le parent du porteur du script est utilise, ou le porteur lui-meme s'il n'a pas de parent.")]
    [SerializeField] private Transform playerRoot;
    [Tooltip("Selection facultative des objets a orienter. Si la liste est vide, tous les enfants directs de Player Root sont orientes automatiquement.")]
    [SerializeField] private Transform[] facingObjects = new Transform[0];

    // Speed at which the object moves (units per second)
    public float moveSpeed = 5f;
    // Distance to consider we've reached the destination
    public float stoppingDistance = 0.1f;

    // Layer mask to use for ground raycasts (set to the ground layer in the Inspector)
    public LayerMask groundLayer = ~0; // default: everything
    // Debug draw the target
    public bool debugDrawTarget = true;

    // Internal state
    Vector3 targetPosition;
    bool moving = false;

    // Min and max bounds for the XZ plane movement
    public float minX = -50f;
    public float maxX = 50f;
    public float minZ = -50f;
    public float maxZ = 50f;

    // Rotation speed in degrees per second when turning to face movement direction
    public float rotationSpeed = 720f;
    // If the model's forward is inverted, set this to true to rotate 180° when facing movement
    public bool invertFacing = false;

    [Header("Retour de la formation")]
    [Tooltip("Vitesse a laquelle les enfants reviennent a leur position locale initiale apres un appui sur Espace.")]
    [Min(0f)] public float formationReturnSpeed = 5f;

    struct ChildInitialPose
    {
        public Transform child;
        public Vector3 localPosition;
        public Quaternion localRotation;
    }

    readonly List<ChildInitialPose> initialChildPoses = new List<ChildInitialPose>();
    bool resettingFormation;
    bool movementEnabled = true;

    private Transform PlayerRoot => playerRoot != null ? playerRoot : transform;
    public bool MovementEnabled => movementEnabled;

    void Awake()
    {
        if (playerRoot == null)
            playerRoot = transform.parent != null ? transform.parent : transform;

        SyntyLocomotionAnimator.EnsureFor(playerRoot.gameObject);
        CaptureInitialChildPoses();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!movementEnabled)
            return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("PlayerMotionCampagne: No main camera found for raycasting.");
            }
            else
            {
                Vector2 mp = mouse.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(mp);
                SetTargetFromRay(ray);
            }
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            StartFormationReset();

        if (moving)
        {
            // Ensure target stays on the same Y as the object so movement is constrained to XZ plane
            targetPosition.y = PlayerRoot.position.y;

            // Compute horizontal direction. The shared root is never rotated because
            // doing so would make offset children travel along an arc.
            Vector3 dir = targetPosition - PlayerRoot.position;
            dir.y = 0f;
            RotateFacingObjects(dir);

            PlayerRoot.position = Vector3.MoveTowards(PlayerRoot.position, targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(PlayerRoot.position, targetPosition) <= stoppingDistance)
            {
                moving = false;
            }
        }
#else
        // Legacy Input Manager
        if (Input.GetMouseButtonDown(1)) // 1 = right mouse button
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("PlayerMotionCampagne: No main camera found for raycasting.");
            }
            else
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                SetTargetFromRay(ray);
            
            }
        }


        if (Input.GetKeyDown(KeyCode.Space))
            StartFormationReset();

        if (moving)
        {
            // Ensure target stays on the same Y as the object so movement is constrained to XZ plane
            targetPosition.y = PlayerRoot.position.y;

            // Compute horizontal direction. The shared root is never rotated because
            // doing so would make offset children travel along an arc.
            Vector3 dir = targetPosition - PlayerRoot.position;
            dir.y = 0f;
            RotateFacingObjects(dir);

            PlayerRoot.position = Vector3.MoveTowards(PlayerRoot.position, targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(PlayerRoot.position, targetPosition) <= stoppingDistance)
            {
                moving = false;
            }
        }
#endif

        UpdateFormationReset();
    }

    public void SetMovementEnabled(bool enabled)
    {
        movementEnabled = enabled;

        if (!enabled)
        {
            moving = false;
            resettingFormation = false;
        }
    }

    void CaptureInitialChildPoses()
    {
        initialChildPoses.Clear();

        for (int i = 0; i < PlayerRoot.childCount; i++)
        {
            Transform child = PlayerRoot.GetChild(i);
            if (IsCameraChild(child))
                continue;

            initialChildPoses.Add(new ChildInitialPose
            {
                child = child,
                localPosition = child.localPosition,
                localRotation = child.localRotation
            });
        }
    }

    void StartFormationReset()
    {
        moving = false;
        resettingFormation = true;
    }

    void UpdateFormationReset()
    {
        if (!resettingFormation)
            return;

        bool resetComplete = true;
        float positionStep = formationReturnSpeed * Time.deltaTime;
        float rotationStep = rotationSpeed * Time.deltaTime;

        for (int i = 0; i < initialChildPoses.Count; i++)
        {
            ChildInitialPose pose = initialChildPoses[i];
            if (pose.child == null)
                continue;

            Vector3 localReturnDirection = pose.localPosition - pose.child.localPosition;
            bool childIsReturning = localReturnDirection.sqrMagnitude > 0.000001f;

            if (childIsReturning)
            {
                Vector3 worldReturnDirection = PlayerRoot.TransformDirection(localReturnDirection);
                worldReturnDirection.y = 0f;

                if (worldReturnDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion returnRotation = Quaternion.LookRotation(worldReturnDirection);
                    if (invertFacing)
                        returnRotation *= Quaternion.Euler(0f, 180f, 0f);

                    RotateFacingObject(pose.child, returnRotation);
                }
            }

            pose.child.localPosition = Vector3.MoveTowards(
                pose.child.localPosition,
                pose.localPosition,
                positionStep);

            // Chaque enfant retrouve son orientation locale initiale une fois
            // revenu a sa place vis-a-vis du parent.
            if (!childIsReturning)
            {
                pose.child.localRotation = Quaternion.RotateTowards(
                    pose.child.localRotation,
                    pose.localRotation,
                    rotationStep);
            }

            if (Vector3.Distance(pose.child.localPosition, pose.localPosition) > 0.001f ||
                Quaternion.Angle(pose.child.localRotation, pose.localRotation) > 0.1f)
            {
                resetComplete = false;
            }
        }

        resettingFormation = !resetComplete;
    }

    void RotateFacingObjects(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        if (invertFacing)
            targetRotation *= Quaternion.Euler(0f, 180f, 0f);

        if (facingObjects != null && facingObjects.Length > 0)
        {
            for (int i = 0; i < facingObjects.Length; i++)
                RotateFacingObject(facingObjects[i], targetRotation);

            return;
        }

        // Sans selection explicite, chaque membre direct de la formation s'oriente
        // autour de son propre pivot. Une camera enfant reste independante.
        for (int i = 0; i < PlayerRoot.childCount; i++)
        {
            Transform child = PlayerRoot.GetChild(i);
            if (IsCameraChild(child))
                continue;

            RotateFacingObject(child, targetRotation);
        }
    }

    bool IsCameraChild(Transform child)
    {
        return child.GetComponent<Camera>() != null || child.GetComponent<PlayerCamera>() != null;
    }

    void RotateFacingObject(Transform facingObject, Quaternion targetRotation)
    {
        // La racine commune ne doit jamais tourner : cela deplacerait ses enfants
        // autour de son pivot et casserait la trajectoire lineaire de la formation.
        if (facingObject == null || facingObject == PlayerRoot)
            return;

        facingObject.rotation = Quaternion.RotateTowards(
            facingObject.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    // Try to set target from a ray: prefer Physics.Raycast against groundLayer, fallback to intersection with horizontal plane at object's Y
    void SetTargetFromRay(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, groundLayer.value);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            // Tous les colliders places sous la racine font partie du joueur.
            if (hit.collider.transform == PlayerRoot || hit.collider.transform.IsChildOf(PlayerRoot))
                continue;

            targetPosition = hit.point;
            // force target onto same Y as the object so movement stays on XZ plane
            targetPosition.y = PlayerRoot.position.y;

            // clamp to movement bounds
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

            resettingFormation = false;
            moving = true;
            return;
        }

        // Fallback: intersect with horizontal plane at object's Y
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, PlayerRoot.position.y, 0f));
        if (groundPlane.Raycast(ray, out float enter))
        {
            targetPosition = ray.GetPoint(enter);
            // force target onto same Y as the object so movement stays on XZ plane
            targetPosition.y = PlayerRoot.position.y;

            // clamp to movement bounds
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

            resettingFormation = false;
            moving = true;
        }
    }

    void OnDrawGizmos()
    {
        if (!debugDrawTarget) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetPosition, 0.25f);
        if (moving)
        {
            Gizmos.DrawLine(PlayerRoot.position, targetPosition);
            // Draw forward marker
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(targetPosition, targetPosition + Vector3.up * 0.5f);
        }
    }
}
