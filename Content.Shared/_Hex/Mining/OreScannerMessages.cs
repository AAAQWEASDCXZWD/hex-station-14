using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Mining;

[Serializable, NetSerializable]
public readonly record struct OreBlipData(short X, short Y, HexOreType Type);

/// <summary>
/// Клієнт запитує дані руди для вказаної консолі (рейтлімітиться на сервері).
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestOreBlipsEvent : EntityEventArgs
{
    public NetEntity ConsoleUid { get; }

    public RequestOreBlipsEvent(NetEntity consoleUid)
    {
        ConsoleUid = consoleUid;
    }
}

/// <summary>
/// Сервер повертає клієнту дані про знайдену руду в навколишніх грідах.
/// </summary>
[Serializable, NetSerializable]
public sealed class GiveOreBlipsEvent : EntityEventArgs
{
    public Dictionary<NetEntity, List<OreBlipData>> Blips { get; }

    public GiveOreBlipsEvent(Dictionary<NetEntity, List<OreBlipData>> blips)
    {
        Blips = blips;
    }
}
