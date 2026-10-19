using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.BUI;

[NetSerializable, Serializable]
public sealed class StoredShuttleInfo
{
    public readonly NetEntity ShuttleUid;
    public readonly string ShuttleName;

    public StoredShuttleInfo(NetEntity shuttleUid, string shuttleName)
    {
        ShuttleUid = shuttleUid;
        ShuttleName = shuttleName;
    }
}

[NetSerializable, Serializable]
public sealed class ShipyardConsoleInterfaceState : BoundUserInterfaceState
{
    public int Balance;
    public readonly bool AccessGranted;
    public readonly string? ShipDeedTitle;
    public int ShipSellValue;
    public readonly bool IsTargetIdPresent;
    public readonly byte UiKey;
    public readonly List<ProtoId<ShuttleAtmospherePrototype>>? AtmosPrototypes;

    public readonly (List<string> available, List<string> unavailable) ShipyardPrototypes;
    public readonly string ShipyardName;
    public readonly bool FreeListings;
    public readonly float SellRate;

    // Bluespace storage fields
    public readonly bool IsShipStored;
    public readonly bool CanStoreShip;
    public readonly bool CanRecallShip;
    public readonly bool HasCaptainAccess;
    public readonly List<StoredShuttleInfo> StoredShuttles;

    public ShipyardConsoleInterfaceState(
        int balance,
        bool accessGranted,
        string? shipDeedTitle,
        int shipSellValue,
        bool isTargetIdPresent,
        byte uiKey,
        (List<string> available, List<string> unavailable) shipyardPrototypes,
        string shipyardName,
        bool freeListings,
        float sellRate,
        List<ProtoId<ShuttleAtmospherePrototype>>? atmosPrototypes,
        bool isShipStored = false,
        bool canStoreShip = false,
        bool canRecallShip = false,
        bool hasCaptainAccess = false,
        List<StoredShuttleInfo>? storedShuttles = null)
    {
        Balance = balance;
        AccessGranted = accessGranted;
        ShipDeedTitle = shipDeedTitle;
        ShipSellValue = shipSellValue;
        IsTargetIdPresent = isTargetIdPresent;
        UiKey = uiKey;
        ShipyardPrototypes = shipyardPrototypes;
        ShipyardName = shipyardName;
        FreeListings = freeListings;
        SellRate = sellRate;
        AtmosPrototypes = atmosPrototypes;
        IsShipStored = isShipStored;
        CanStoreShip = canStoreShip;
        CanRecallShip = canRecallShip;
        HasCaptainAccess = hasCaptainAccess;
        StoredShuttles = storedShuttles ?? new();
    }
}
