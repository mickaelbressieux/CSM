using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses a stone so a player can read its powers at a glance. Lives on the stone prefab root
/// alongside <see cref="Stone"/>, and is driven once at spawn by <see cref="StoneLoadout.ApplyTo"/>.
///
/// <b>The visual grammar.</b> A power's <see cref="PowerCategory"/> — not the power itself — decides
/// *where* its art goes, and that mapping exists in exactly one place (<see cref="SocketFor"/>):
///
/// <list type="bullet">
/// <item><see cref="PowerCategory.Activated"/> → an <b>antenna</b> above the handle</item>
/// <item><see cref="PowerCategory.PassiveSelf"/> → the <b>stone body</b> is swapped</item>
/// <item><see cref="PowerCategory.PassiveOther"/> → a small <b>flag</b> on the rim</item>
/// </list>
///
/// Each power then supplies its own mesh within that channel, so every activated power wears an
/// antenna but no two antennas look alike. A player learns the grammar once and it holds for every
/// power added later.
///
/// <b>A body swap never touches physics.</b> The "stone" child carries the convex MeshCollider and
/// the ice physic material — that is the stone's collision body. Swapping the look therefore only
/// disables its <see cref="Renderer"/> and parents a replacement mesh alongside it; the collider
/// stays live. Every stone collides identically no matter what it carries, which keeps the physics
/// fair and predictable. Body prefabs must accordingly be visual-only, with no collider of their own.
///
/// <b>Scale.</b> The stone root is scaled to 0.06, so anything parented under it inherits that.
/// Give each socket a localScale of ~16.667 (1 / 0.06) and art can be authored at real metre scale.
/// </summary>
public class StoneVisuals : MonoBehaviour
{
    [Header("Sockets (children of the stone root)")]
    [Tooltip("Above the handle. Holds antennas for Activated powers.")]
    [SerializeField] private Transform antennaSocket;

    [Tooltip("At the body origin. Holds the replacement mesh for a PassiveSelf power.")]
    [SerializeField] private Transform bodySocket;

    [Tooltip("On the rim. Holds flags for PassiveOther powers.")]
    [SerializeField] private Transform flagSocket;

    [Header("Body")]
    [Tooltip("The 'stone' child's renderer. Hidden (not deactivated) when a power swaps the body, " +
             "so its MeshCollider and physic material stay in play.")]
    [SerializeField] private Renderer bodyRenderer;

    [Header("Layout")]
    [Tooltip("Sideways spacing, in socket-local units, between powers sharing one socket.")]
    [SerializeField] private float attachmentSpacing = 0.05f;

    // Which power swapped the body, so a second one can be reported clearly rather than fighting it.
    private StonePowerDefinition bodySwappedBy;

    /// <summary>
    /// Dress the stone for the powers it carries. Called once with the whole set — not per power —
    /// so the layout of stacked attachments and the "only one body" rule are resolved in one place.
    /// Safe to call with an empty list: the stone simply keeps its default look.
    /// </summary>
    public void Apply(IReadOnlyList<StonePowerDefinition> powers)
    {
        if (powers == null)
            return;

        // Count per socket first, so a group of attachments can be centred rather than growing
        // off to one side as powers are added.
        Dictionary<Transform, int> total = new Dictionary<Transform, int>();
        for (int i = 0; i < powers.Count; i++)
        {
            StonePowerDefinition power = powers[i];
            if (power == null || power.VisualPrefab == null) continue;
            if (power.Category == PowerCategory.PassiveSelf) continue;   // body is not laid out

            Transform socket = SocketFor(power.Category);
            if (socket == null) continue;
            total[socket] = total.TryGetValue(socket, out int n) ? n + 1 : 1;
        }

        Dictionary<Transform, int> placed = new Dictionary<Transform, int>();

        for (int i = 0; i < powers.Count; i++)
        {
            StonePowerDefinition power = powers[i];
            if (power == null)
                continue;

            if (power.VisualPrefab == null)
            {
                Debug.LogWarning(
                    $"{name}: power '{power.DisplayName}' has no visualPrefab, so it will look like " +
                    "an ordinary stone. Assign one on the power asset.", this);
                continue;
            }

            Transform socket = SocketFor(power.Category);
            if (socket == null)
            {
                Debug.LogWarning(
                    $"{name}: no {power.Category} socket wired on this stone prefab, so " +
                    $"'{power.DisplayName}' cannot be shown. Check the StoneVisuals fields.", this);
                continue;
            }

            if (power.Category == PowerCategory.PassiveSelf)
            {
                ApplyBody(power, socket);
                continue;
            }

            int index = placed.TryGetValue(socket, out int n) ? n : 0;
            placed[socket] = index + 1;
            Attach(power, socket, index, total[socket]);
        }
    }

    /// <summary>
    /// The visual grammar, in one place: a power's category decides which socket its art hangs on.
    /// Change the language here and every power follows.
    /// </summary>
    private Transform SocketFor(PowerCategory category)
    {
        switch (category)
        {
            case PowerCategory.Activated:    return antennaSocket;
            case PowerCategory.PassiveSelf:  return bodySocket;
            case PowerCategory.PassiveOther: return flagSocket;
            default:                         return null;
        }
    }

    // Hang one antenna / flag, spread sideways so stacked powers read as several rather than
    // z-fighting into one. The group stays centred on the socket whatever its size.
    private void Attach(StonePowerDefinition power, Transform socket, int index, int count)
    {
        GameObject visual = Instantiate(power.VisualPrefab, socket);
        visual.name = $"Visual_{power.DisplayName}";

        float offset = (index - (count - 1) * 0.5f) * attachmentSpacing;
        visual.transform.localPosition = new Vector3(offset, 0f, 0f);
        visual.transform.localRotation = Quaternion.identity;
    }

    // Swap the look of the stone body. Only the FIRST PassiveSelf power wins - a stone cannot wear
    // two bodies - and the renderer is disabled rather than the GameObject, so the collider and its
    // physic material survive untouched.
    private void ApplyBody(StonePowerDefinition power, Transform socket)
    {
        if (bodySwappedBy != null)
        {
            Debug.LogWarning(
                $"{name}: '{power.DisplayName}' also wants to change the stone body, but " +
                $"'{bodySwappedBy.DisplayName}' already did. A stone can only wear one body; " +
                "the first one wins.", this);
            return;
        }

        bodySwappedBy = power;

        GameObject visual = Instantiate(power.VisualPrefab, socket);
        visual.name = $"Body_{power.DisplayName}";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        if (bodyRenderer != null)
            bodyRenderer.enabled = false;   // NOT SetActive(false): that would take the collider too
        else
            Debug.LogWarning(
                $"{name}: bodyRenderer is not wired, so the default stone mesh will show through " +
                $"'{power.DisplayName}'. Point it at the 'stone' child's MeshRenderer.", this);
    }

    // Editor convenience: wire the obvious references when the component is first added, so setting
    // up the second stone prefab is near-free. Never runs at runtime.
    private void Reset()
    {
        antennaSocket = transform.Find("AntennaSocket");
        bodySocket    = transform.Find("BodySocket");
        flagSocket    = transform.Find("FlagSocket");

        // GetComponentInChildren, not GetComponent: the "stone" child is a nested prefab from
        // stone.blend whose mesh (and MeshCollider) sit on a child of it, not on the object itself.
        Transform body = transform.Find("stone");
        if (body != null) bodyRenderer = body.GetComponentInChildren<Renderer>();
    }
}
