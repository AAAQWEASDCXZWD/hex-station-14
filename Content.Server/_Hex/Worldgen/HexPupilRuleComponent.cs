namespace Content.Server._Hex.Worldgen;

/// <summary>
/// Component for the Hex-Pupil game rule, managing custom sector generation:
/// Frontier station in the center, two designated trade outposts, and an asteroid layout
/// scaled according to the mathematical hex grid.
/// </summary>
[RegisterComponent]
public sealed partial class HexPupilRuleComponent : Component
{
    /// <summary>
    /// UIDs of the spawned trade outposts for this round.
    /// </summary>
    public List<EntityUid> TradePosts = new();
}
