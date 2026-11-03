namespace Content.Server._Hex.Internet;

/// <summary>
///     Marks a cartridge as the Internet program. The actual feed lives in <see cref="InternetSystem"/>
///     so every PDA sees the same posts.
/// </summary>
[RegisterComponent]
public sealed partial class InternetCartridgeComponent : Component
{
}
