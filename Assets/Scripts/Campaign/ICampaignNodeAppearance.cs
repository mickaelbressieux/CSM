/// <summary>
/// Where a campaign node is in the campaign. Add cases here (Locked, InProgress, ...) as the
/// campaign grows; every appearance is handed the state and decides how to show it.
/// </summary>
public enum CampaignNodeState
{
    /// <summary>Walking into it starts a match.</summary>
    Available,

    /// <summary>Its match has been played; it no longer does anything.</summary>
    Cleared
}

/// <summary>
/// How a campaign node shows what state it is in. <see cref="CampaignMatchNode"/> owns the
/// state and delegates the look, so the two can change independently.
///
/// Today the nodes are cubes and <see cref="TintedNodeAppearance"/> just recolours them. When
/// they become character models, write an appearance that drives an Animator instead - sit
/// down when cleared, stand when available - and swap which component is wired into the node.
/// No other code changes.
/// </summary>
public interface ICampaignNodeAppearance
{
    void Apply(CampaignNodeState state);
}
