using Content.Shared.CartridgeLoader;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Internet;

[Serializable, NetSerializable]
public sealed class InternetCreatePostMessage : CartridgeMessageEvent
{
    public readonly InternetPostType Type;
    public readonly int ParentId;
    public readonly string Title;
    public readonly string Body;
    public readonly byte[]? ImageData;

    public InternetCreatePostMessage(InternetPostType type, int parentId, string title, string body, byte[]? imageData)
    {
        Type = type;
        ParentId = parentId;
        Title = title;
        Body = body;
        ImageData = imageData;
    }
}

[Serializable, NetSerializable]
public sealed class InternetEditPostMessage : CartridgeMessageEvent
{
    public readonly int PostId;
    public readonly string Title;
    public readonly string Body;
    public readonly byte[]? ImageData;

    public InternetEditPostMessage(int postId, string title, string body, byte[]? imageData)
    {
        PostId = postId;
        Title = title;
        Body = body;
        ImageData = imageData;
    }
}

[Serializable, NetSerializable]
public sealed class InternetLikePostMessage : CartridgeMessageEvent
{
    public readonly int PostId;

    public InternetLikePostMessage(int postId)
    {
        PostId = postId;
    }
}

[Serializable, NetSerializable]
public sealed class InternetCreateCommentMessage : CartridgeMessageEvent
{
    public readonly int PostId;
    public readonly int ParentCommentId;
    public readonly string Text;

    public InternetCreateCommentMessage(int postId, int parentCommentId, string text)
    {
        PostId = postId;
        ParentCommentId = parentCommentId;
        Text = text;
    }
}

[Serializable, NetSerializable]
public sealed class InternetLikeCommentMessage : CartridgeMessageEvent
{
    public readonly int PostId;
    public readonly int CommentId;

    public InternetLikeCommentMessage(int postId, int commentId)
    {
        PostId = postId;
        CommentId = commentId;
    }
}

[Serializable, NetSerializable]
public sealed class InternetSendChatMessage : CartridgeMessageEvent
{
    public readonly int PostId;
    public readonly string Text;
    public readonly byte[]? ImageData;

    public InternetSendChatMessage(int postId, string text, byte[]? imageData)
    {
        PostId = postId;
        Text = text;
        ImageData = imageData;
    }
}

/// <summary>
///     Requests a fresh state from the server, for example after the PDA's ID card changes.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetDeletePostMessage : CartridgeMessageEvent
{
    public readonly int PostId;

    public InternetDeletePostMessage(int postId)
    {
        PostId = postId;
    }
}

[Serializable, NetSerializable]
public sealed class InternetSetAliasMessage : CartridgeMessageEvent
{
    public readonly string Alias;

    public InternetSetAliasMessage(string alias)
    {
        Alias = alias;
    }
}

/// <summary>
///     Admin-only: removes every post authored by the target account on the current network.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetAdminDeleteUserPostsMessage : CartridgeMessageEvent
{
    public readonly NetUserId TargetUserId;

    public InternetAdminDeleteUserPostsMessage(NetUserId targetUserId)
    {
        TargetUserId = targetUserId;
    }
}

[Serializable, NetSerializable]
public sealed class InternetRefreshMessage : CartridgeMessageEvent
{
    public readonly int Revision = 1;
}
