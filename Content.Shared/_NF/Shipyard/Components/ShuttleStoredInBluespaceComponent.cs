using Robust.Shared.GameStates;

namespace Content.Shared._NF.Shipyard.Components;

/// <summary>
/// Added to a shuttle grid while it is kept in bluespace storage.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleStoredInBluespaceComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StoredTime;

    [DataField, AutoNetworkedField]
    public EntityUid? StationUid;

    [DataField, AutoNetworkedField]
    public string? StoredBy;

    [DataField, AutoNetworkedField]
    public byte[]? PhotoData;
}
