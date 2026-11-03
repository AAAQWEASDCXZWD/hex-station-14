namespace Content.Shared._Hex.Internet;

/// <summary>
///     Shared limits and image choices for the Internet program.
/// </summary>
public static class InternetDefaults
{
    public const int MaxTitleLength = 64;
    public const int MaxBodyLength = 2000;
    public const int MaxCommentLength = 500;
    public const int MaxChatMessageLength = 500;
    public const int MaxCommentDepth = 3;
    public const int MaxTopicDepth = 5;
    public const int MaxPosts = 500;
    public const int MaxCommentsPerPost = 500;
    public const int MaxMessagesPerChat = 500;

    /// <summary>
    ///     Emblem states that can be attached to posts and chat messages.
    /// </summary>
    public static readonly string[] Images =
    {
        "shield", "star", "circle", "diamond", "hexagon", "triangle", "cross", "ring",
    };

    public const string EmblemRsiPath = "/Textures/_Hex/Interface/Faction/emblems.rsi";

    public static bool IsValidImage(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            return false;

        return Array.IndexOf(Images, imageId) >= 0;
    }
}
