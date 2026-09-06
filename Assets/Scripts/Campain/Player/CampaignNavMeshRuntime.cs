using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Garantit qu'une surface navigable existe sur la carte de campagne. Une surface preparee dans
/// l'Inspector est reutilisee. En son absence, une surface de secours est construite une seule fois
/// a partir des colliders du decor.
/// </summary>
public static class CampaignNavMeshRuntime
{
    const string RuntimeSurfaceName = "Campaign Runtime NavMesh";

    public static bool EnsureBuilt(Transform playerRoot, LayerMask sourceLayers, float sampleRadius)
    {
        if (IsPositionNavigable(playerRoot.position, sampleRadius))
            return true;

        NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject surfaceObject = new GameObject(RuntimeSurfaceName);
            surface = surfaceObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = sourceLayers;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        }

        Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(true);
        bool[] colliderStates = new bool[playerColliders.Length];

        // La formation du joueur ne doit pas etre integree comme obstacle dans sa propre surface.
        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider collider = playerColliders[i];
            colliderStates[i] = collider != null && collider.enabled;
            if (collider != null)
                collider.enabled = false;
        }

        try
        {
            surface.BuildNavMesh();
        }
        finally
        {
            for (int i = 0; i < playerColliders.Length; i++)
            {
                Collider collider = playerColliders[i];
                if (collider != null)
                    collider.enabled = colliderStates[i];
            }
        }

        bool built = IsPositionNavigable(playerRoot.position, sampleRadius);
        if (!built)
        {
            Debug.LogError(
                "CampaignNavMeshRuntime: aucune zone navigable n'a ete trouvee pres du joueur. " +
                "Verifie que le sol et les batiments possedent des colliders inclus dans Navigation Source Layers.",
                playerRoot);
        }

        return built;
    }

    public static bool TrySample(Vector3 position, float radius, out NavMeshHit hit)
    {
        return NavMesh.SamplePosition(position, out hit, Mathf.Max(0.1f, radius), NavMesh.AllAreas);
    }

    /// <summary>
    /// Projette les deux extremites sur le NavMesh et ne renvoie qu'un chemin complet.
    /// Les scripts de mouvement utilisent ainsi tous la meme validation et ne retombent jamais
    /// silencieusement sur un deplacement en ligne droite a travers le decor.
    /// </summary>
    public static bool TryCalculateCompletePath(
        Vector3 startPosition,
        Vector3 requestedDestination,
        float sampleRadius,
        int areaMask,
        out NavMeshPath path,
        out Vector3 sampledDestination)
    {
        path = null;
        sampledDestination = requestedDestination;
        float radius = Mathf.Max(0.1f, sampleRadius);

        if (!NavMesh.SamplePosition(startPosition, out NavMeshHit startHit, radius, areaMask) ||
            !NavMesh.SamplePosition(requestedDestination, out NavMeshHit destinationHit, radius, areaMask))
        {
            return false;
        }

        NavMeshPath candidate = new NavMeshPath();
        if (!NavMesh.CalculatePath(startHit.position, destinationHit.position, areaMask, candidate) ||
            candidate.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        path = candidate;
        sampledDestination = destinationHit.position;
        return true;
    }

    static bool IsPositionNavigable(Vector3 position, float radius)
    {
        return TrySample(position, radius, out _);
    }
}
