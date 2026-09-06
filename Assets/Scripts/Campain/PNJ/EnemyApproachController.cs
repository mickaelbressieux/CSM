using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Prend temporairement la main sur le deplacement existant d'un PNJ pour amener ses racines
/// jusqu'au joueur. Les enfants ne sont jamais deplaces individuellement : ils suivent leur
/// racine et conservent donc leur position locale et leur animation.
/// </summary>
public class EnemyApproachController : MonoBehaviour
{
    [Tooltip("Racines a deplacer ensemble. Si la liste est vide, utilise le Transform de ce composant.")]
    [SerializeField] Transform[] movingRoots = Array.Empty<Transform>();
    [Min(0f)] [SerializeField] float moveSpeed = 3f;
    [Min(0f)] [SerializeField] float stoppingDistance = 1.75f;
    [SerializeField] bool constrainToXZ = true;
    [Min(0f)] [SerializeField] float rotationSpeed = 720f;

    readonly List<BehaviourState> suspendedMotions = new List<BehaviourState>();
    Transform target;
    Action arrivedCallback;
    bool approaching;
    NavMeshPath approachPath;
    int pathCornerIndex;

    struct BehaviourState
    {
        public Behaviour behaviour;
        public bool wasEnabled;
    }

    public bool IsApproaching => approaching;

    public void BeginApproach(Transform newTarget, Action onArrived)
    {
        if (newTarget == null)
        {
            onArrived?.Invoke();
            return;
        }

        target = newTarget;
        arrivedCallback = onArrived;
        SuspendExistingMotion();

        if (DistanceToTarget() <= stoppingDistance)
        {
            approaching = true;
            FinishApproach();
            return;
        }

        if (!PrepareApproachPath())
        {
            Debug.LogWarning($"{name}: aucun chemin NavMesh accessible jusqu'a la cible de l'embuscade.", this);
            approaching = true;
            FinishApproach();
            return;
        }

        approaching = true;
    }

    public void CancelApproach(bool restoreExistingMotion = true)
    {
        approaching = false;
        target = null;
        arrivedCallback = null;
        approachPath = null;
        pathCornerIndex = 0;

        if (restoreExistingMotion)
            RestoreExistingMotion();
    }

    void Update()
    {
        if (!approaching || target == null)
            return;

        Transform primaryRoot = GetPrimaryRoot();
        if (primaryRoot == null)
        {
            FinishApproach();
            return;
        }

        if (approachPath == null || approachPath.corners.Length == 0)
        {
            if (!PrepareApproachPath())
            {
                FinishApproach();
                return;
            }
        }

        float remainingDistance = RemainingPathDistance(primaryRoot.position);
        if (remainingDistance <= stoppingDistance)
        {
            FinishApproach();
            return;
        }

        Vector3[] corners = approachPath.corners;
        while (pathCornerIndex < corners.Length - 1 &&
               Vector3.Distance(primaryRoot.position, corners[pathCornerIndex]) <= 0.05f)
        {
            pathCornerIndex++;
        }

        Vector3 toWaypoint = corners[Mathf.Clamp(pathCornerIndex, 0, corners.Length - 1)] - primaryRoot.position;
        if (constrainToXZ)
            toWaypoint.y = 0f;
        if (toWaypoint.sqrMagnitude <= 0.0001f)
            return;

        float travel = Mathf.Min(moveSpeed * Time.deltaTime, remainingDistance - stoppingDistance);
        Vector3 delta = toWaypoint.normalized * Mathf.Min(travel, toWaypoint.magnitude);
        MoveAllRoots(delta, toWaypoint);
    }

    bool PrepareApproachPath()
    {
        Transform primaryRoot = GetPrimaryRoot();
        if (primaryRoot == null || target == null)
            return false;

        if (!CampaignNavMeshRuntime.TryCalculateCompletePath(
                primaryRoot.position,
                target.position,
                2f,
                NavMesh.AllAreas,
                out approachPath,
                out _))
        {
            approachPath = null;
            pathCornerIndex = 0;
            return false;
        }

        pathCornerIndex = approachPath.corners.Length > 1 ? 1 : 0;
        return true;
    }

    float RemainingPathDistance(Vector3 currentPosition)
    {
        if (approachPath == null || approachPath.corners.Length == 0)
            return 0f;

        Vector3[] corners = approachPath.corners;
        int cornerIndex = Mathf.Clamp(pathCornerIndex, 0, corners.Length - 1);
        float distance = Vector3.Distance(currentPosition, corners[cornerIndex]);
        for (int i = cornerIndex + 1; i < corners.Length; i++)
            distance += Vector3.Distance(corners[i - 1], corners[i]);

        return distance;
    }

    Transform GetPrimaryRoot()
    {
        if (movingRoots != null)
        {
            for (int i = 0; i < movingRoots.Length; i++)
                if (movingRoots[i] != null)
                    return movingRoots[i];
        }

        return transform;
    }

    void MoveAllRoots(Vector3 delta, Vector3 direction)
    {
        bool hasConfiguredRoot = false;
        if (movingRoots != null)
        {
            for (int i = 0; i < movingRoots.Length; i++)
            {
                Transform root = movingRoots[i];
                if (root == null)
                    continue;

                hasConfiguredRoot = true;
                MoveRoot(root, delta, direction);
            }
        }

        if (!hasConfiguredRoot)
            MoveRoot(transform, delta, direction);
    }

    void MoveRoot(Transform root, Vector3 delta, Vector3 direction)
    {
        root.position += delta;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(flatDirection);
        root.rotation = Quaternion.RotateTowards(
            root.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    float DistanceToTarget()
    {
        Transform primaryRoot = GetPrimaryRoot();
        if (primaryRoot == null || target == null)
            return 0f;

        Vector3 from = primaryRoot.position;
        Vector3 to = target.position;
        if (constrainToXZ)
        {
            from.y = 0f;
            to.y = 0f;
        }

        return Vector3.Distance(from, to);
    }

    void FinishApproach()
    {
        approaching = false;
        target = null;
        approachPath = null;
        pathCornerIndex = 0;
        Action callback = arrivedCallback;
        arrivedCallback = null;

        // Le mouvement aleatoire/lineaire reste suspendu pendant la discussion et le chargement.
        callback?.Invoke();
    }

    void SuspendExistingMotion()
    {
        RestoreExistingMotion();

        bool inspectedConfiguredRoot = false;
        if (movingRoots != null)
        {
            for (int i = 0; i < movingRoots.Length; i++)
            {
                Transform root = movingRoots[i];
                if (root == null)
                    continue;

                inspectedConfiguredRoot = true;
                SyntyLocomotionAnimator.EnsureFor(root.gameObject);
                AddMotions(root.GetComponentsInChildren<PNJMotionLinear>(true));
                AddMotions(root.GetComponentsInChildren<PNJMotionRandom>(true));
            }
        }

        if (!inspectedConfiguredRoot)
        {
            SyntyLocomotionAnimator.EnsureFor(gameObject);
            AddMotions(GetComponentsInChildren<PNJMotionLinear>(true));
            AddMotions(GetComponentsInChildren<PNJMotionRandom>(true));
        }
    }

    void AddMotions<T>(T[] motions) where T : Behaviour
    {
        for (int i = 0; i < motions.Length; i++)
        {
            T motion = motions[i];
            if (motion == null || ContainsSuspendedMotion(motion))
                continue;

            suspendedMotions.Add(new BehaviourState
            {
                behaviour = motion,
                wasEnabled = motion.enabled
            });
            motion.enabled = false;
        }
    }

    bool ContainsSuspendedMotion(Behaviour motion)
    {
        for (int i = 0; i < suspendedMotions.Count; i++)
            if (suspendedMotions[i].behaviour == motion)
                return true;

        return false;
    }

    void RestoreExistingMotion()
    {
        for (int i = 0; i < suspendedMotions.Count; i++)
        {
            BehaviourState state = suspendedMotions[i];
            if (state.behaviour != null)
                state.behaviour.enabled = state.wasEnabled;
        }
        suspendedMotions.Clear();
    }

    void OnDisable()
    {
        if (approaching)
            CancelApproach();
    }

    void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
    }
}
