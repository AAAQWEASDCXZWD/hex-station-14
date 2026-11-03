using Content.Shared._Hex.Internet;

namespace Content.Server._Hex.Internet;

/// <summary>
///     Marks a cartridge as an Internet/Darknet program. The actual feed lives in <see cref="InternetSystem"/>
///     so every PDA sees the same posts for the selected network.
/// </summary>
[RegisterComponent]
public sealed partial class InternetCartridgeComponent : Component
{
    /// <summary>
    ///     Which isolated network this cartridge displays.
    /// </summary>
    [DataField]
    public InternetNetwork Network = InternetNetwork.Internet;

    /// <summary>
    ///     Whether this is the admin build, which unlocks moderation tools for admins.
    /// </summary>
    [DataField]
    public bool Admin;
}
