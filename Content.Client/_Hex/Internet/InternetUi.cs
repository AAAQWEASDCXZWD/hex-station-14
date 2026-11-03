using Content.Client._Hex.Internet.UI;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Hex.Internet;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Hex.Internet;

/// <summary>
///     UI fragment for the Internet PDA program.
/// </summary>
[Virtual]
public partial class InternetUi : UIFragment
{
    private InternetWindow? _window;
    private BoundUserInterface? _userInterface;

    protected virtual InternetNetwork Network => InternetNetwork.Internet;
    protected virtual bool AdminMode => false;

    public override Control GetUIFragmentRoot()
    {
        return _window!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        // Setup is called every time the loader state is refreshed. Keep a live window so the
        // player does not lose their current feed position or drafts, but recreate it after the
        // previous window was disposed when the cartridge was closed or a new UI took over.
        if (_window is { Disposed: false } && ReferenceEquals(_userInterface, userInterface))
            return;

        _userInterface = userInterface;
        _window = new InternetWindow(Network, AdminMode);

        _window.OnCreatePost += (type, parentId, title, body, imageData) =>
            Send(userInterface, new InternetCreatePostMessage(type, parentId, title, body, imageData));

        _window.OnEditPost += (postId, title, body, imageData) =>
            Send(userInterface, new InternetEditPostMessage(postId, title, body, imageData));

        _window.OnLikePost += postId =>
            Send(userInterface, new InternetLikePostMessage(postId));

        _window.OnAddComment += (postId, parentCommentId, text) =>
            Send(userInterface, new InternetCreateCommentMessage(postId, parentCommentId, text));

        _window.OnLikeComment += (postId, commentId) =>
            Send(userInterface, new InternetLikeCommentMessage(postId, commentId));

        _window.OnSendChatMessage += (postId, text, imageData) =>
            Send(userInterface, new InternetSendChatMessage(postId, text, imageData));

        _window.OnDeletePost += postId =>
            Send(userInterface, new InternetDeletePostMessage(postId));

        _window.OnSetAlias += alias =>
            Send(userInterface, new InternetSetAliasMessage(alias));

        _window.OnAdminDeleteUserPosts += userId =>
            Send(userInterface, new InternetAdminDeleteUserPostsMessage(userId));

        _window.OnRefreshRequested += () =>
            Send(userInterface, new InternetRefreshMessage());
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is InternetUiState internetState && _window is { Disposed: false } window)
            window.UpdateState(internetState);
    }

    private static void Send(BoundUserInterface userInterface, CartridgeMessageEvent message)
    {
        userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
