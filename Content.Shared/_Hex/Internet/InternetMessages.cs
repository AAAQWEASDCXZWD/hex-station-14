using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Internet;

[Serializable, NetSerializable]
public sealed class InternetCreatePostMessage : CartridgeMessageEvent
{
    public readonly InternetPostType Type;
    public readonly int ParentId;
    public readonly string Title;
    public readonly string Body;
    public readonly string? ImageId;

    public InternetCreatePostMessage(InternetPostType type, int parentId, string title, string body, string? imageId)
    {
        Type = type;
        ParentId = parentId;
        Title = title;
        Body = body;
        ImageId = imageId;
    }
}

[Serializable, NetSerializable]
public sealed class InternetEditPostMessage : CartridgeMessageEvent
{
    public readonly int PostId;
    public readonly string Title;
    public readonly string Body;
    public readonly string? ImageId;

    public InternetEditPostMessage(int postId, string title, string body, string? imageId)
    {
        PostId = postId;
        Title = title;
        Body = body;
        ImageId = imageId;
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
    public readonly string? ImageId;

    public InternetSendChatMessage(int postId, string text, string? imageId)
    {
        PostId = postId;
        Text = text;
        ImageId = imageId;
    }
}

/// <summary>
///     Requests a fresh state from the server, for example after the PDA's ID card changes.
/// </summary>
[Serializable, NetSerializable]
public sealed class InternetRefreshMessage : CartridgeMessageEvent
{
}
