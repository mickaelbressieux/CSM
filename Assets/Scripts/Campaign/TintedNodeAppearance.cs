using UnityEngine;

/// <summary>
/// Shows a node's state by recolouring its renderer: red while a match is available, blue once
/// it has been played.
///
/// This is the placeholder-cube appearance. When the nodes become character models, leave this
/// class alone and write an Animator-driven <see cref="ICampaignNodeAppearance"/> beside it,
/// then point the node's Appearance Source at that instead.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class TintedNodeAppearance : MonoBehaviour, ICampaignNodeAppearance
{
    [SerializeField] private Color availableColor = new Color(0.78f, 0.16f, 0.18f);
    [SerializeField] private Color clearedColor = new Color(0.17f, 0.40f, 0.78f);

    private Renderer nodeRenderer;

    private void Awake()
    {
        nodeRenderer = GetComponent<Renderer>();
    }

    public void Apply(CampaignNodeState state)
    {
        if (nodeRenderer == null)
        {
            return;
        }

        Color color = state == CampaignNodeState.Cleared ? clearedColor : availableColor;

        // .material, not .sharedMaterial: this instances the material so recolouring one node
        // does not repaint every other node using the same asset.
        Material material = nodeRenderer.material;
        material.color = color;

        // URP's Lit shader reads _BaseColor; Material.color does not always map onto it.
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }
}
