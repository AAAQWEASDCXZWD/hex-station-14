namespace Content.Server._Hex.Internet;

/// <summary>
///     The Darknet alias bound to a specific ID card. Server-only; the alias never has to
///     match the card's or player's real name.
/// </summary>
[RegisterComponent]
public sealed partial class DarknetAliasComponent : Component
{
    [DataField]
    public string Alias = string.Empty;
}
