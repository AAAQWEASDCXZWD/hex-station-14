namespace Content.Shared._Hex.Internet;

/// <summary>
///     Shared limits for the Internet program.
/// </summary>
public static class InternetDefaults
{
    public const int MaxAliasLength = 32;
    public const int MaxTitleLength = 64;
    public const int MaxBodyLength = 1200;
    public const int MaxCommentLength = 500;
    public const int MaxChatMessageLength = 500;
    public const int MaxCommentDepth = 3;
    public const int MaxTopicDepth = 5;
    public const int MaxPosts = 200;
    public const int MaxCommentsPerPost = 200;
    public const int MaxMessagesPerChat = 200;

    /// <summary>
    ///     Uploaded images are decoded, scaled down and re-encoded as PNG on the client.
    /// </summary>
    public const int MaxImageDimension = 128;
    public const int MaxImageBytes = 32 * 1024;

    /// <summary>
    ///     Total image payload kept for the whole round, so a full feed cannot become arbitrarily large.
    /// </summary>
    public const int MaxTotalImageBytes = 512 * 1024;
}
