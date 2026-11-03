using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._Hex.Internet;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Hex.Internet;

/// <summary>
///     The shared, round-scoped "Internet" shown by the Internet cartridge.
/// </summary>
/// <remarks>
///     The feed is intentionally not stored on the cartridge itself: every PDA with the program has to
///     see the same posts, comments and chats. The data is kept in memory for the duration of a round.
/// </remarks>
public sealed class InternetSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private static readonly TimeSpan WriteCooldown = TimeSpan.FromMilliseconds(500);

    private readonly List<InternetPost> _posts = new();
    private readonly Dictionary<int, InternetPost> _postsById = new();
    private readonly HashSet<EntityUid> _openLoaders = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextWrite = new();

    private int _nextPostId = 1;
    private int _nextCommentId = 1;
    private int _nextMessageId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeActivatedEvent>(OnActivated);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    #region Cartridge lifecycle

    private void OnActivated(EntityUid uid, InternetCartridgeComponent component, CartridgeActivatedEvent args)
    {
        _openLoaders.Add(args.Loader);
    }

    private void OnDeactivated(EntityUid uid, InternetCartridgeComponent component, CartridgeDeactivatedEvent args)
    {
        _openLoaders.Remove(args.Loader);
    }

    private void OnRemoved(EntityUid uid, InternetCartridgeComponent component, CartridgeRemovedEvent args)
    {
        _openLoaders.Remove(args.Loader);
    }

    private void OnUiReady(EntityUid uid, InternetCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        _openLoaders.Add(args.Loader);
        SendState(args.Loader);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _posts.Clear();
        _postsById.Clear();
        _openLoaders.Clear();
        _nextWrite.Clear();
        _nextPostId = 1;
        _nextCommentId = 1;
        _nextMessageId = 1;
    }

    #endregion

    #region Messages

    private void OnMessage(EntityUid uid, InternetCartridgeComponent component, CartridgeMessageEvent args)
    {
        switch (args)
        {
            case InternetCreatePostMessage create:
                CreatePost(create);
                break;
            case InternetEditPostMessage edit:
                EditPost(edit);
                break;
            case InternetLikePostMessage like:
                LikePost(like);
                break;
            case InternetCreateCommentMessage comment:
                CreateComment(comment);
                break;
            case InternetLikeCommentMessage likeComment:
                LikeComment(likeComment);
                break;
            case InternetSendChatMessage chat:
                SendChatMessage(chat);
                break;
            case InternetRefreshMessage refresh:
                SendState(GetEntity(refresh.LoaderUid));
                break;
        }
    }

    private void CreatePost(InternetCreatePostMessage message)
    {
        if (!TryGetAuthor(message, out var actor, out var userId, out var userName, out var characterName, out var idCardName))
            return;

        if (!TryConsumeWrite(userId))
            return;

        var title = (message.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            Reject(actor, "internet-popup-title-required");
            return;
        }

        if (title.Length > InternetDefaults.MaxTitleLength)
            title = title[..InternetDefaults.MaxTitleLength];

        var body = (message.Body ?? string.Empty).Trim();
        if (body.Length > InternetDefaults.MaxBodyLength)
            body = body[..InternetDefaults.MaxBodyLength];

        InternetPost? parent = null;
        if (message.ParentId != 0)
        {
            if (!_postsById.TryGetValue(message.ParentId, out var foundParent))
                return;

            if (foundParent.Type != InternetPostType.Topic)
            {
                Reject(actor, "internet-popup-invalid-parent");
                return;
            }

            if (GetDepth(foundParent) + 1 > InternetDefaults.MaxTopicDepth)
            {
                Reject(actor, "internet-popup-too-deep");
                return;
            }

            parent = foundParent;
        }

        if (_posts.Count >= InternetDefaults.MaxPosts)
        {
            Reject(actor, "internet-popup-limit");
            return;
        }

        var post = new InternetPost
        {
            Id = _nextPostId++,
            Type = message.Type,
            ParentId = parent?.Id ?? 0,
            Title = title,
            Body = body,
            ImageId = NormalizeImage(message.ImageId),
            AuthorName = characterName,
            AuthorUserId = userId,
            AuthorUserName = userName,
            CreatedAt = Now(),
        };

        _posts.Insert(0, post);
        _postsById[post.Id] = post;

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(actor):user} created {post.Type} '{title}' ({idCardName}) on the Internet");

        Broadcast();
    }

    private void EditPost(InternetEditPostMessage message)
    {
        if (!TryGetAuthor(message, out var actor, out var userId, out var userName, out var characterName, out var idCardName))
            return;

        if (!_postsById.TryGetValue(message.PostId, out var post))
            return;

        if (!CanEdit(post, userId, userName, characterName, idCardName))
        {
            Reject(actor, "internet-popup-not-author");
            return;
        }

        if (!TryConsumeWrite(userId))
            return;

        var title = (message.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            Reject(actor, "internet-popup-title-required");
            return;
        }

        if (title.Length > InternetDefaults.MaxTitleLength)
            title = title[..InternetDefaults.MaxTitleLength];

        var body = (message.Body ?? string.Empty).Trim();
        if (body.Length > InternetDefaults.MaxBodyLength)
            body = body[..InternetDefaults.MaxBodyLength];

        post.Title = title;
        post.Body = body;
        post.ImageId = NormalizeImage(message.ImageId);
        post.EditedAt = Now();

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(actor):user} edited {post.Type} '{title}' ({idCardName}) on the Internet");

        Broadcast();
    }

    private void LikePost(InternetLikePostMessage message)
    {
        if (!TryGetUserId(message.Actor, out var userId))
            return;

        if (!_postsById.TryGetValue(message.PostId, out var post))
            return;

        ToggleLike(post.LikedBy, userId);
        Broadcast();
    }

    private void CreateComment(InternetCreateCommentMessage message)
    {
        if (!TryGetAuthor(message, out var actor, out var userId, out _, out var characterName, out var idCardName))
            return;

        if (!_postsById.TryGetValue(message.PostId, out var post))
            return;

        if (post.Type != InternetPostType.Post)
            return;

        if (!TryConsumeWrite(userId))
            return;

        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            Reject(actor, "internet-popup-message-required");
            return;
        }

        if (text.Length > InternetDefaults.MaxCommentLength)
            text = text[..InternetDefaults.MaxCommentLength];

        if (message.ParentCommentId != 0)
        {
            var parent = post.Comments.FirstOrDefault(comment => comment.Id == message.ParentCommentId);
            if (parent == null)
                return;

            if (GetCommentDepth(post, parent) + 1 > InternetDefaults.MaxCommentDepth)
            {
                Reject(actor, "internet-popup-comment-too-deep");
                return;
            }
        }

        if (post.Comments.Count >= InternetDefaults.MaxCommentsPerPost)
        {
            Reject(actor, "internet-popup-limit");
            return;
        }

        post.Comments.Add(new InternetComment
        {
            Id = _nextCommentId++,
            ParentId = message.ParentCommentId,
            AuthorName = characterName,
            Text = text,
            CreatedAt = Now(),
        });

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(actor):user} commented on Internet post '{post.Title}' ({idCardName})");

        Broadcast();
    }

    private void LikeComment(InternetLikeCommentMessage message)
    {
        if (!TryGetUserId(message.Actor, out var userId))
            return;

        if (!_postsById.TryGetValue(message.PostId, out var post))
            return;

        var comment = post.Comments.FirstOrDefault(entry => entry.Id == message.CommentId);
        if (comment == null)
            return;

        ToggleLike(comment.LikedBy, userId);
        Broadcast();
    }

    private void SendChatMessage(InternetSendChatMessage message)
    {
        if (!TryGetAuthor(message, out var actor, out var userId, out _, out var characterName, out var idCardName))
            return;

        if (!_postsById.TryGetValue(message.PostId, out var post) || post.Type != InternetPostType.Chat)
            return;

        if (!TryConsumeWrite(userId))
            return;

        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length > InternetDefaults.MaxChatMessageLength)
            text = text[..InternetDefaults.MaxChatMessageLength];

        var imageId = NormalizeImage(message.ImageId);
        if (text.Length == 0 && imageId == null)
        {
            Reject(actor, "internet-popup-message-required");
            return;
        }

        if (post.Messages.Count >= InternetDefaults.MaxMessagesPerChat)
        {
            Reject(actor, "internet-popup-limit");
            return;
        }

        post.Messages.Add(new InternetChatMessage
        {
            Id = _nextMessageId++,
            AuthorName = characterName,
            Text = text,
            ImageId = imageId,
            CreatedAt = Now(),
        });

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(actor):user} wrote in Internet chat '{post.Title}' ({idCardName})");

        Broadcast();
    }

    #endregion

    #region Validation

    private bool TryGetAuthor(
        CartridgeMessageEvent message,
        out EntityUid actor,
        out NetUserId userId,
        out string userName,
        out string characterName,
        out string idCardName)
    {
        actor = message.Actor;
        userId = default;
        userName = string.Empty;
        characterName = string.Empty;
        idCardName = string.Empty;

        if (!TryGetUserId(actor, out userId, out userName))
            return false;

        var loader = GetEntity(message.LoaderUid);
        if (!loader.Valid || TerminatingOrDeleted(loader))
            return false;

        // The card that authorises a post is the one inside the PDA running the program, not
        // an arbitrary ID card the user may be carrying in a different slot.
        if (!_idCards.TryGetIdCard(loader, out Entity<IdCardComponent> card))
        {
            Reject(actor, "internet-popup-no-id");
            return false;
        }

        idCardName = card.Comp.FullName ?? string.Empty;
        characterName = Name(actor);

        if (string.IsNullOrWhiteSpace(idCardName))
        {
            Reject(actor, "internet-popup-no-id");
            return false;
        }

        if (idCardName != characterName)
        {
            Reject(actor, "internet-popup-id-mismatch");
            return false;
        }

        return true;
    }

    private bool TryGetUserId(EntityUid actor, out NetUserId userId)
    {
        return TryGetUserId(actor, out userId, out _);
    }

    private bool TryGetUserId(EntityUid actor, out NetUserId userId, out string userName)
    {
        userId = default;
        userName = string.Empty;

        if (!actor.Valid || TerminatingOrDeleted(actor))
            return false;

        if (!_player.TryGetSessionByEntity(actor, out var session))
            return false;

        userId = session.UserId;
        userName = session.Name;
        return true;
    }

    private static bool CanEdit(
        InternetPost post,
        NetUserId userId,
        string userName,
        string characterName,
        string idCardName)
    {
        return post.AuthorUserId == userId
               && post.AuthorUserName == userName
               && post.AuthorName == characterName
               && post.AuthorName == idCardName;
    }

    private bool TryConsumeWrite(NetUserId userId)
    {
        var now = _timing.CurTime;
        if (_nextWrite.TryGetValue(userId, out var next) && now < next)
            return false;

        _nextWrite[userId] = now + WriteCooldown;
        return true;
    }

    private int GetDepth(InternetPost post)
    {
        var depth = 1;
        var current = post;
        var guard = 0;

        while (current.ParentId != 0 && guard++ < 100)
        {
            if (!_postsById.TryGetValue(current.ParentId, out var parent))
                break;

            depth++;
            current = parent;
        }

        return depth;
    }

    private static int GetCommentDepth(InternetPost post, InternetComment comment)
    {
        var depth = 0;
        var current = comment;
        var guard = 0;

        while (current.ParentId != 0 && guard++ < 100)
        {
            var parent = post.Comments.FirstOrDefault(entry => entry.Id == current.ParentId);
            if (parent == null)
                break;

            depth++;
            current = parent;
        }

        return depth;
    }

    private static string? NormalizeImage(string? imageId)
    {
        return InternetDefaults.IsValidImage(imageId) ? imageId : null;
    }

    private static void ToggleLike(HashSet<NetUserId> likes, NetUserId userId)
    {
        if (!likes.Add(userId))
            likes.Remove(userId);
    }

    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private void Reject(EntityUid actor, string locKey)
    {
        _popup.PopupEntity(Loc.GetString(locKey), actor, actor);
    }

    #endregion

    #region State

    private void Broadcast()
    {
        if (_openLoaders.Count == 0)
            return;

        var snapshot = CreateSnapshot();

        foreach (var loader in _openLoaders.ToArray())
        {
            if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) ||
                loaderComp.ActiveProgram is not { } program ||
                !HasComp<InternetCartridgeComponent>(program))
            {
                _openLoaders.Remove(loader);
                continue;
            }

            SendState(loader, loaderComp, snapshot);
        }
    }

    private void SendState(EntityUid loader, CartridgeLoaderComponent? loaderComp = null, List<InternetPostInfo>? snapshot = null)
    {
        if (!Resolve(loader, ref loaderComp))
            return;

        if (loaderComp.ActiveProgram is not { } program || !HasComp<InternetCartridgeComponent>(program))
            return;

        snapshot ??= CreateSnapshot();

        var hasIdCard = _idCards.TryGetIdCard(loader, out Entity<IdCardComponent> card)
                        && !string.IsNullOrWhiteSpace(card.Comp.FullName);

        var state = new InternetUiState(
            snapshot,
            hasIdCard,
            hasIdCard ? card.Comp.FullName : null);

        _cartridge.UpdateCartridgeUiState(loader, state);
    }

    private List<InternetPostInfo> CreateSnapshot()
    {
        var snapshot = new List<InternetPostInfo>(_posts.Count);

        foreach (var post in _posts)
        {
            var info = new InternetPostInfo
            {
                Id = post.Id,
                Type = post.Type,
                ParentId = post.ParentId,
                Title = post.Title,
                Body = post.Body,
                ImageId = post.ImageId,
                AuthorName = post.AuthorName,
                AuthorUserId = post.AuthorUserId,
                CreatedAt = post.CreatedAt,
                EditedAt = post.EditedAt,
            };

            info.LikedBy.AddRange(post.LikedBy);

            foreach (var comment in post.Comments.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id))
            {
                var commentInfo = new InternetCommentInfo
                {
                    Id = comment.Id,
                    ParentId = comment.ParentId,
                    AuthorName = comment.AuthorName,
                    Text = comment.Text,
                    CreatedAt = comment.CreatedAt,
                };

                commentInfo.LikedBy.AddRange(comment.LikedBy);
                info.Comments.Add(commentInfo);
            }

            foreach (var message in post.Messages.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id))
            {
                info.Messages.Add(new InternetChatMessageInfo
                {
                    Id = message.Id,
                    AuthorName = message.AuthorName,
                    Text = message.Text,
                    ImageId = message.ImageId,
                    CreatedAt = message.CreatedAt,
                });
            }

            snapshot.Add(info);
        }

        return snapshot;
    }

    #endregion

    #region Data

    private sealed class InternetPost
    {
        public int Id;
        public InternetPostType Type;
        public int ParentId;
        public string Title = string.Empty;
        public string Body = string.Empty;
        public string? ImageId;
        public string AuthorName = string.Empty;
        public NetUserId AuthorUserId;
        public string AuthorUserName = string.Empty;
        public long CreatedAt;
        public long EditedAt;
        public readonly HashSet<NetUserId> LikedBy = new();
        public readonly List<InternetComment> Comments = new();
        public readonly List<InternetChatMessage> Messages = new();
    }

    private sealed class InternetComment
    {
        public int Id;
        public int ParentId;
        public string AuthorName = string.Empty;
        public string Text = string.Empty;
        public long CreatedAt;
        public readonly HashSet<NetUserId> LikedBy = new();
    }

    private sealed class InternetChatMessage
    {
        public int Id;
        public string AuthorName = string.Empty;
        public string Text = string.Empty;
        public string? ImageId;
        public long CreatedAt;
    }

    #endregion
}
