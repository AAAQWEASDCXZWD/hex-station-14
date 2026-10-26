using Content.Shared._NF.ShuttleRecords;
using Robust.Shared.Analyzers;
using Robust.Shared.GameStates;

namespace Content.Shared._NF.Shipyard.Components;

/// <summary>
/// Tied to an ID card when a ship is purchased. 1 ship per captain.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedShipyardSystem), typeof(SharedShuttleRecordsSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class ShuttleDeedComponent : Component
{
    public const int MaxNameLength = 30;
    public const int MaxSuffixLength = 3 + 1 + 4; // 3 digits, dash, up to 4 letters - should be enough

    [DataField, AutoNetworkedField]
    public EntityUid? ShuttleUid = null;

    [DataField, AutoNetworkedField]
    public string? ShuttleName = "Unknown";

    [DataField("shuttleSuffix"), AutoNetworkedField]
    public string? ShuttleNameSuffix;

    [DataField, AutoNetworkedField]
    public string? ShuttleOwner = "Unknown";

    [DataField, AutoNetworkedField]
    public Robust.Shared.Network.NetUserId? OwnerUserId;

    [DataField, AutoNetworkedField]
    public EntityUid? OwnerEntity;

    [DataField, AutoNetworkedField]
    public bool PurchasedWithVoucher;

    [DataField, AutoNetworkedField]
    public bool IsStored;

    [DataField, AutoNetworkedField]
    [Access(typeof(SharedShipyardSystem), typeof(SharedShuttleRecordsSystem), Other = AccessPermissions.ReadExecute)]
    public List<EntityUid> OwnedShuttles = new();

    [DataField, AutoNetworkedField]
    public TimeSpan CooldownEndTime = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);

    [DataField, AutoNetworkedField]
    public byte[]? PhotoData;
}
