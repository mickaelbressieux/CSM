using UnityEngine;
using UnityEngine.AI;
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

    [Header("Navigation")]
    [Tooltip("Couches utilisees pour construire le NavMesh de secours. Elles doivent contenir le sol et les colliders des batiments.")]
    [SerializeField] LayerMask navigationSourceLayers = ~0;
    [Tooltip("Marge autour de toute la formation pour eviter de frotter contre les murs.")]
    [Min(0.05f)] [SerializeField] float navigationRadius = 1.25f;
    [Min(0.1f)] [SerializeField] float navigationHeight = 2f;
    [Tooltip("Distance maximale utilisee pour ramener un clic sur une zone navigable proche.")]
    [Min(0.1f)] [SerializeField] float clickProjectionRadius = 3f;
    [Min(0f)] [SerializeField] float acceleration = 30f;

    // Layer mask to use for ground raycasts (set to the ground layer in the Inspector)
    public LayerMask groundLayer = ~0; // default: everything
    // Debug draw the target
    public bool debugDrawTarget = true;

    // Internal state
    Vector3 targetPosition;
    bool moving = false;
    NavMeshAgent navigationAgent;

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
    [Tooltip("Distance de projection sur le NavMesh pour le retour individuel des membres de la formation.")]
    [Min(0.1f)] [SerializeField] float formationPathProjectionRadius = 0.75f;

    sealed class ChildInitialPose
    {
        public Transform child;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public NavMeshPath returnPath;
        public int returnCornerIndex;
        public float nextPathRetryTime;
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

        CaptureInitialChildPoses();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeNavigation();
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

#endif

        UpdateNavigationMovement();
        UpdateFormationReset();
    }

    public void SetMovementEnabled(bool enabled)
    {
        movementEnabled = enabled;

        if (!enabled)
        {
            moving = false;
            resettingFormation = false;
            StopNavigation();
        }
        else if (navigationAgent != null && navigationAgent.isOnNavMesh)
        {
            navigationAgent.isStopped = false;
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

            // Chaque personnage mesure son propre mouvement. Le pilote place sur la racine
            // commune ne voyait pas le retour individuel et les laissait glisser en idle.
            SyntyLocomotionAnimator.EnsureFor(child.gameObject);

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
        StopNavigation();
        resettingFormation = true;

        for (int i = 0; i < initialChildPoses.Count; i++)
            PrepareChildReturnPath(initialChildPoses[i]);
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

            Vector3 desiredWorldPosition = PlayerRoot.TransformPoint(pose.localPosition);
            bool childIsReturning = Vector3.Distance(pose.child.position, desiredWorldPosition) > 0.001f;

            if (childIsReturning)
            {
                if (pose.returnPath == null && Time.time >= pose.nextPathRetryTime)
                    PrepareChildReturnPath(pose);

                Vector3 worldReturnDirection = MoveChildAlongReturnPath(pose, positionStep);

                if (worldReturnDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion returnRotation = Quaternion.LookRotation(worldReturnDirection);
                    if (invertFacing)
                        returnRotation *= Quaternion.Euler(0f, 180f, 0f);

                    RotateFacingObject(pose.child, returnRotation);
                }
            }

            // Chaque enfant retrouve son orientation locale initiale une fois
            // revenu a sa place vis-a-vis du parent.
            if (!childIsReturning)
            {
                pose.child.localPosition = pose.localPosition;
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

    void PrepareChildReturnPath(ChildInitialPose pose)
    {
        if (pose == null || pose.child == null)
            return;

        Vector3 destination = PlayerRoot.TransformPoint(pose.localPosition);
        if (CampaignNavMeshRuntime.TryCalculateCompletePath(
                pose.child.position,
                destination,
                formationPathProjectionRadius,
                NavMesh.AllAreas,
                out NavMeshPath path,
                out _))
        {
            pose.returnPath = path;
            pose.returnCornerIndex = path.corners.Length > 1 ? 1 : 0;
            return;
        }

        pose.returnPath = null;
        pose.returnCornerIndex = 0;
        pose.nextPathRetryTime = Time.time + 0.5f;
        if (debugDrawTarget)
            Debug.LogWarning($"PlayerMotionCampagne: aucun chemin accessible pour remettre '{pose.child.name}' en formation.", pose.child);
    }

    Vector3 MoveChildAlongReturnPath(ChildInitialPose pose, float positionStep)
    {
        if (pose.returnPath == null || pose.returnPath.corners.Length == 0)
            return Vector3.zero;

        Vector3[] corners = pose.returnPath.corners;
        while (pose.returnCornerIndex < corners.Length - 1 &&
               Vector3.Distance(pose.child.position, corners[pose.returnCornerIndex]) <= 0.05f)
        {
            pose.returnCornerIndex++;
        }

        Vector3 waypoint = corners[Mathf.Clamp(pose.returnCornerIndex, 0, corners.Length - 1)];
        Vector3 direction = waypoint - pose.child.position;
        direction.y = 0f;
        pose.child.position = Vector3.MoveTowards(pose.child.position, waypoint, positionStep);

        if (pose.returnCornerIndex >= corners.Length - 1 &&
            Vector3.Distance(pose.child.position, waypoint) <= 0.001f)
        {
            // La projection NavMesh peut corriger legerement la hauteur. La pose locale exacte
            // n'est restauree qu'apres avoir parcouru l'integralite du chemin valide.
            pose.child.localPosition = pose.localPosition;
            pose.returnPath = null;
        }

        return direction;
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

            if (TrySetDestination(hit.point))
                return;
        }

        // Fallback: intersect with horizontal plane at object's Y
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, PlayerRoot.position.y, 0f));
        if (groundPlane.Raycast(ray, out float enter))
        {
            TrySetDestination(ray.GetPoint(enter));
        }
    }

    void InitializeNavigation()
    {
        if (!CampaignNavMeshRuntime.EnsureBuilt(PlayerRoot, navigationSourceLayers, clickProjectionRadius))
            return;

        if (!CampaignNavMeshRuntime.TrySample(PlayerRoot.position, clickProjectionRadius, out NavMeshHit startHit))
            return;

        // Positionne d'abord la racine sur la surface. Ajouter un agent hors NavMesh provoque sinon
        // un avertissement et empeche SetDestination de fonctionner.
        PlayerRoot.position = startHit.position;
        navigationAgent = PlayerRoot.GetComponent<NavMeshAgent>();
        if (navigationAgent == null)
            navigationAgent = PlayerRoot.gameObject.AddComponent<NavMeshAgent>();

        navigationAgent.updateRotation = false;
        navigationAgent.updateUpAxis = true;
        navigationAgent.speed = moveSpeed;
        navigationAgent.acceleration = acceleration;
        navigationAgent.angularSpeed = rotationSpeed;
        navigationAgent.stoppingDistance = stoppingDistance;
        navigationAgent.radius = navigationRadius;
        navigationAgent.height = navigationHeight;
        navigationAgent.autoBraking = true;

        if (!navigationAgent.isOnNavMesh)
            navigationAgent.Warp(startHit.position);
    }

    bool TrySetDestination(Vector3 requestedPosition)
    {
        if (navigationAgent == null || !navigationAgent.isOnNavMesh)
        {
            Debug.LogWarning("PlayerMotionCampagne: le NavMeshAgent n'est pas pret.", this);
            return false;
        }

        requestedPosition.x = Mathf.Clamp(requestedPosition.x, minX, maxX);
        requestedPosition.z = Mathf.Clamp(requestedPosition.z, minZ, maxZ);

        if (!CampaignNavMeshRuntime.TrySample(requestedPosition, clickProjectionRadius, out NavMeshHit destinationHit))
            return false;

        if (!CampaignNavMeshRuntime.TryCalculateCompletePath(
                navigationAgent.transform.position,
                destinationHit.position,
                clickProjectionRadius,
                navigationAgent.areaMask,
                out NavMeshPath path,
                out Vector3 sampledDestination))
        {
            if (debugDrawTarget)
                Debug.Log("PlayerMotionCampagne: destination inaccessible, deplacement ignore.", this);
            return false;
        }

        targetPosition = sampledDestination;
        resettingFormation = false;
        navigationAgent.isStopped = false;
        moving = navigationAgent.SetPath(path);
        return moving;
    }

    void UpdateNavigationMovement()
    {
        if (!moving || navigationAgent == null || !navigationAgent.isOnNavMesh)
            return;

        navigationAgent.speed = moveSpeed;
        navigationAgent.acceleration = acceleration;
        navigationAgent.stoppingDistance = stoppingDistance;

        Vector3 direction = navigationAgent.desiredVelocity;
        direction.y = 0f;
        RotateFacingObjects(direction);

        if (navigationAgent.pathPending)
            return;

        if (!navigationAgent.hasPath || navigationAgent.remainingDistance <= stoppingDistance)
        {
            moving = false;
            navigationAgent.ResetPath();
        }
    }

    void StopNavigation()
    {
        if (navigationAgent == null || !navigationAgent.isOnNavMesh)
            return;

        navigationAgent.isStopped = true;
        navigationAgent.ResetPath();
    }

    void OnDrawGizmos()
    {
        if (!debugDrawTarget) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetPosition, 0.25f);
        if (moving)
        {
            if (navigationAgent != null && navigationAgent.hasPath)
            {
                Vector3[] corners = navigationAgent.path.corners;
                for (int i = 1; i < corners.Length; i++)
                    Gizmos.DrawLine(corners[i - 1], corners[i]);
            }
            else
            {
                Gizmos.DrawLine(PlayerRoot.position, targetPosition);
            }
            // Draw forward marker
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(targetPosition, targetPosition + Vector3.up * 0.5f);
        }
    }
}
