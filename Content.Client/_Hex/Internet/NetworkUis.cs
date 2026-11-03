using Content.Shared._Hex.Internet;

namespace Content.Client._Hex.Internet;

/// <summary>
///     Darknet variant of the Internet program. Uses the same client window with the red/black theme.
/// </summary>
public sealed partial class DarknetUi : InternetUi
{
    protected override InternetNetwork Network => InternetNetwork.Darknet;
}

/// <summary>
///     Admin Internet program. The same UI unlocks moderation tools for actual admins.
/// </summary>
public sealed partial class AdminInternetUi : InternetUi
{
    protected override bool AdminMode => true;
}

/// <summary>
///     Admin Darknet program.
/// </summary>
public sealed partial class AdminDarknetUi : InternetUi
{
    protected override InternetNetwork Network => InternetNetwork.Darknet;
    protected override bool AdminMode => true;
}
