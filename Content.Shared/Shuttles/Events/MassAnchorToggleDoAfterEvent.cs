using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

/// <summary>
/// Raised after the delay of a mass anchor toggle, carrying the state the anchor should end up in.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class MassAnchorToggleDoAfterEvent : DoAfterEvent
{
    public bool Enabled { get; set; }

    private MassAnchorToggleDoAfterEvent()
    {
    }

    public MassAnchorToggleDoAfterEvent(bool enabled)
    {
        Enabled = enabled;
    }

    public override DoAfterEvent Clone() => new MassAnchorToggleDoAfterEvent(Enabled);
}
