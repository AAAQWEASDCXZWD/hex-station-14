using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     Attached to a character entity and records which faction the character belongs to, as a separate
///     parameter on the character itself (the faction registry also keeps a member list for terminals).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionMemberComponent : Component
{
    [DataField, AutoNetworkedField]
    public string FactionId = string.Empty;

    [DataField, AutoNetworkedField]
    public string FactionName = string.Empty;

    [DataField, AutoNetworkedField]
    public FactionRole Role = FactionRole.Member;
}
