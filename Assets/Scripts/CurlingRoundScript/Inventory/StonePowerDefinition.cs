using UnityEngine;

/// <summary>
/// A power in the catalogue, authored as a ScriptableObject asset
/// (Create ▸ Curling ▸ Powers ▸ ...). One asset = one buyable/equippable power.
///
/// It carries two things:
/// <list type="bullet">
/// <item><b>Presentation + identity</b> — id, name, description, icon. Everything a shop or an
/// inventory screen needs, so story mode will not need a parallel lookup table.</item>
/// <item><b>How to become real</b> — <see cref="AttachTo"/> adds the matching
/// <see cref="StoneAbility"/> component to a spawned stone and copies this asset's tuning onto it.
/// Tuning therefore lives on the asset (balance it without touching code), while behaviour lives
/// in the ability component.</item>
/// </list>
///
/// Adding a fourth power is: one ability class + one definition subclass + one asset. No enum to
/// extend and no factory switch to remember — nothing shared is edited.
///
/// <b>Timing contract:</b> <see cref="AttachTo"/> is called while the stone GameObject is still
/// INACTIVE, so the new components are present before Awake/OnEnable run and
/// <see cref="Stone.Abilities"/> caches them. See <c>SoloCurlingGameManager.BuildStone</c>.
/// </summary>
public abstract class StonePowerDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField, Tooltip("Stable key used by save data and (later) the story-mode shop. " +
        "Leave empty to use the asset's file name. Never rename in place once saves exist.")]
    private string powerId;

    [Header("Presentation")]
    [SerializeField, Tooltip("Name shown in the HUD and inventory. Leave empty for this power's " +
        "built-in default name.")]
    private string displayName;

    [TextArea] public string description;
    public Sprite icon;

    [Header("Visual")]
    [SerializeField, Tooltip("The art for this power. Where it goes on the stone is decided by " +
        "Category — antenna, stone body, or flag. Leave empty and the power still works, just " +
        "without a distinctive look.")]
    private GameObject visualPrefab;

    /// <summary>What kind of power this is. Intrinsic to the power rather than an authorable field,
    /// so it can never be misconfigured per asset. It also picks the visual channel — see
    /// <see cref="StoneVisuals"/> — and will group the story-mode shop.</summary>
    public abstract PowerCategory Category { get; }

    /// <summary>This power's art, hung on the socket its <see cref="Category"/> dictates.
    /// Null is fine: the power works, it just has no distinctive look yet.</summary>
    public GameObject VisualPrefab => visualPrefab;

    /// <summary>Stable save/shop key. Falls back to the asset's file name.</summary>
    public string PowerId => string.IsNullOrWhiteSpace(powerId) ? name : powerId;

    /// <summary>Name for the HUD and inventory screens. An empty field falls back to
    /// <see cref="DefaultDisplayName"/>, so a freshly created asset already reads correctly —
    /// there is no shared placeholder that every power would report.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName;

    /// <summary>The name used when the asset's field is left empty. Subclasses override it with a
    /// human-readable name for their power; the asset's file name is the last resort.</summary>
    protected virtual string DefaultDisplayName => name;

    /// <summary>Add this power to a stone and copy the asset's tuning onto the component.
    /// Returns the created ability so callers can inspect it.</summary>
    public abstract StoneAbility AttachTo(GameObject stone);

    /// <summary>Shared plumbing for subclasses: adds the component and stamps the display name.
    /// A subclass then copies its own tuning fields onto the returned instance.</summary>
    protected T Attach<T>(GameObject stone) where T : StoneAbility
    {
        T ability = stone.AddComponent<T>();
        ability.PowerName = DisplayName;
        return ability;
    }
}
