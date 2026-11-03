using Content.Client._Hex.Internet.UI;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Hex.Internet;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Hex.Internet;

/// <summary>
///     UI fragment for the Internet PDA program.
/// </summary>
public sealed partial class InternetUi : UIFragment
{
    private InternetWindow? _window;

    public override Control GetUIFragmentRoot()
    {
        return _window!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        // Setup is called every time the loader state is refreshed. Keep the existing window so the
        // player does not lose their current feed position or drafts.
        if (_window != null)
            return;

        _window = new InternetWindow();

        _window.OnCreatePost += (type, parentId, title, body, imageId) =>
            Send(userInterface, new InternetCreatePostMessage(type, parentId, title, body, imageId));

        _window.OnEditPost += (postId, title, body, imageId) =>
            Send(userInterface, new InternetEditPostMessage(postId, title, body, imageId));

        _window.OnLikePost += postId =>
            Send(userInterface, new InternetLikePostMessage(postId));

        _window.OnAddComment += (postId, parentCommentId, text) =>
            Send(userInterface, new InternetCreateCommentMessage(postId, parentCommentId, text));

        _window.OnLikeComment += (postId, commentId) =>
            Send(userInterface, new InternetLikeCommentMessage(postId, commentId));

        _window.OnSendChatMessage += (postId, text, imageId) =>
            Send(userInterface, new InternetSendChatMessage(postId, text, imageId));

        _window.OnRefreshRequested += () =>
            Send(userInterface, new InternetRefreshMessage());
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is InternetUiState internetState)
            _window?.UpdateState(internetState);
    }

    private static void Send(BoundUserInterface userInterface, CartridgeMessageEvent message)
    {
        userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
