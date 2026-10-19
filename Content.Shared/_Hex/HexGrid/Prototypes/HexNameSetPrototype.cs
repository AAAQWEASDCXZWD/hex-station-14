using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.HexGrid.Prototypes;

/// <summary>
/// Configures the names displayed for sectors of the global hex grid.
/// </summary>
[Prototype("hexNameSet")]
public sealed partial class HexNameSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<HexNameEntry> Hexes { get; private set; } = [];
}

/// <summary>
/// A display name for one axial hex coordinate.
/// </summary>
[DataDefinition]
public sealed partial class HexNameEntry
{
    [DataField(required: true)]
    public int Q { get; private set; }

    [DataField(required: true)]
    public int R { get; private set; }

    [DataField(required: true)]
    public string Name { get; private set; } = string.Empty;
}
