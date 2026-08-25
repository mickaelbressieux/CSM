using UnityEngine;

/// <summary>
/// A place on the campaign map that starts a match, and remembers that it did.
///
/// It sits next to a <see cref="SceneTransitionTrigger"/> and listens for that trigger
/// committing to a transition. At that moment it records where the player was standing, so the
/// campaign can put them back after the match, and marks itself cleared in
/// <see cref="CampainManager"/> - which persists across the trip to the curling scene, so on
/// the way back the node comes up inert.
///
/// It owns state, never appearance: what "cleared" looks like is delegated to an
/// <see cref="ICampaignNodeAppearance"/>. Today that recolours a cube; later it can sit a
/// character down without this class changing.
/// </summary>
[RequireComponent(typeof(SceneTransitionTrigger))]
public class CampaignMatchNode : MonoBehaviour
{
    [Tooltip("Stable id used to remember this node. Falls back to the object's name if empty; " +
             "set it explicitly if you rename or duplicate nodes.")]
    [SerializeField] private string nodeId;

    // A component implementing ICampaignNodeAppearance. Serialized as a MonoBehaviour so any
    // appearance can be wired in from the inspector without this node naming a concrete type.
    [SerializeField] private MonoBehaviour appearanceSource;

    [Tooltip("Extra behaviours to switch off once this node's match has been played. Scripts " +
             "on this object are stopped automatically; use this for ones on child objects, " +
             "such as the pieces of a character rig.")]
    [SerializeField] private Behaviour[] alsoDisableWhenCleared = System.Array.Empty<Behaviour>();

    private SceneTransitionTrigger trigger;

    private string NodeId => string.IsNullOrWhiteSpace(nodeId) ? gameObject.name : nodeId;

    public bool IsCleared =>
        CampainManager.Instance != null && CampainManager.Instance.IsNodeCleared(NodeId);

    private void Awake()
    {
        trigger = GetComponent<SceneTransitionTrigger>();
    }

    private void OnEnable()
    {
        trigger.TransitionStarted += HandleTransitionStarted;
    }

    private void OnDisable()
    {
        trigger.TransitionStarted -= HandleTransitionStarted;
    }

    private void Start()
    {
        // Start, not Awake: CampainManager sets up its singleton in Awake, and on the very
        // first load of the campaign we could otherwise ask before it exists.
        Apply(IsCleared ? CampaignNodeState.Cleared : CampaignNodeState.Available);
    }

    private void HandleTransitionStarted(GameObject enteringObject)
    {
        CampainManager manager = CampainManager.Instance;
        if (manager == null)
        {
            return;
        }

        // The root, because the collider that entered may be on a child of the player rig.
        Vector3 playerPosition = enteringObject.transform.root.position;
        Camera camera = Camera.main;
        manager.StoreReturnPoint(playerPosition, camera != null ? camera.transform.position : (Vector3?)null);

        manager.MarkNodeCleared(NodeId);
    }

    private void Apply(CampaignNodeState state)
    {
        bool cleared = state == CampaignNodeState.Cleared;

        // Disarm rather than disable: a disabled MonoBehaviour still receives OnTriggerEnter,
        // so switching the component off would leave the node quietly starting matches.
        trigger.IsArmed = !cleared;

        if (cleared)
        {
            StopClearedBehaviours();
        }

        ResolveAppearance()?.Apply(state);
    }

    /// <summary>
    /// A played opponent stays put. Everything on this object that is not transition
    /// machinery counts as the node's own behaviour - the patrol and wander scripts today,
    /// whatever drives a character model later - and all of it stops.
    ///
    /// Discovered rather than listed, so a node that gains a new script does the right thing
    /// without anyone remembering to wire it up. If something genuinely has to keep running on
    /// a cleared node, that is what the <see cref="ICampaignNodeAppearance"/> is for: it is
    /// excluded here precisely so it can react to being cleared.
    /// </summary>
    private void StopClearedBehaviours()
    {
        foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
        {
            if (!IsTransitionMachinery(behaviour))
            {
                behaviour.enabled = false;
            }
        }

        foreach (Behaviour behaviour in alsoDisableWhenCleared)
        {
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }
    }

    private bool IsTransitionMachinery(MonoBehaviour behaviour)
    {
        return behaviour == null
            || behaviour == this
            || behaviour is SceneTransitionTrigger
            || behaviour is ITransitionEffect
            || behaviour is ICampaignNodeAppearance;
    }

    private ICampaignNodeAppearance ResolveAppearance()
    {
        if (appearanceSource == null)
        {
            return null;
        }

        if (appearanceSource is ICampaignNodeAppearance appearance)
        {
            return appearance;
        }

        Debug.LogError(
            $"{nameof(CampaignMatchNode)}: assigned appearanceSource does not implement ICampaignNodeAppearance.",
            this);
        return null;
    }
}
