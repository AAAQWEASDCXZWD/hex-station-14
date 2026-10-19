using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.HexGrid;

/// <summary>
/// Sent by the server to provide the current runtime names of all hexes.
/// </summary>
[Serializable, NetSerializable]
public sealed class HexGridNamesUpdatedEvent(List<HexGridName> names) : EntityEventArgs
{
    public readonly List<HexGridName> Names = names;
}

[Serializable, NetSerializable]
public readonly record struct HexGridName(int Q, int R, string Name);
