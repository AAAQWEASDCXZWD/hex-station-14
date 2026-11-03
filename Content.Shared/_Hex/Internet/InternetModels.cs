using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Internet;

/// <summary>
///     The kind of entry that can be posted on the Internet.
/// </summary>
[Serializable, NetSerializable]
public enum InternetPostType : byte
{
    Post,
    Topic,
    Chat,
}

/// <summary>
///     A single comment on an Internet post. Comments can be nested through <see cref="ParentId"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetCommentInfo
{
    public int Id;
    public int ParentId;
    public string AuthorName = string.Empty;
    public string Text = string.Empty;
    public long CreatedAt;
    public List<NetUserId> LikedBy = new();
}

/// <summary>
///     A single message in a chat post.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetChatMessageInfo
{
    public int Id;
    public string AuthorName = string.Empty;
    public string Text = string.Empty;
    public string? ImageId;
    public long CreatedAt;
}

/// <summary>
///     A snapshot of one Internet post sent to clients.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetPostInfo
{
    public int Id;
    public InternetPostType Type;
    public int ParentId;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public string? ImageId;
    public string AuthorName = string.Empty;
    public NetUserId AuthorUserId;
    public long CreatedAt;
    public long EditedAt;
    public List<NetUserId> LikedBy = new();
    public List<InternetCommentInfo> Comments = new();
    public List<InternetChatMessageInfo> Messages = new();
}

/// <summary>
///     Full state of the Internet program. The same state is sent to every open PDA; per-viewer
///     information (likes) is stored as user ids so clients can compare against their local user.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetUiState : BoundUserInterfaceState
{
    public readonly List<InternetPostInfo> Posts;
    public readonly bool HasIdCard;
    public readonly string? IdCardName;

    public InternetUiState(List<InternetPostInfo> posts, bool hasIdCard, string? idCardName)
    {
        Posts = posts;
        HasIdCard = hasIdCard;
        IdCardName = idCardName;
    }
}
