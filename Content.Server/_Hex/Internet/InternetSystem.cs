using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Shared._Hex.Internet;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Hex.Internet;

/// <summary>
///     Shared, round-scoped Internet and Darknet networks shown by the corresponding cartridges.
/// </summary>
/// <remarks>
///     The networks are stored separately and never share posts, topics, comments or identities.
///     Internet posts require an ID card matching the character; Darknet posts use an alias bound
///     to the ID card.
/// </remarks>
public sealed class InternetSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly TimeSpan WriteCooldown = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<InternetNetwork, NetworkState> _networks = new()
    {
        [InternetNetwork.Internet] = new NetworkState(),
        [InternetNetwork.Darknet] = new NetworkState(),
    };

    private readonly HashSet<EntityUid> _openLoaders = new();
    private readonly Dictionary<(InternetNetwork Network, NetUserId UserId), TimeSpan> _nextWrite = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeActivatedEvent>(OnActivated);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<InternetCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<EntInsertedIntoContainerMessage>(OnIdCardInserted);
        SubscribeLocalEvent<EntRemovedFromContainerMessage>(OnIdCardRemoved);
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
        SendState(args.Loader, program: component);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        foreach (var state in _networks.Values)
            state.Reset();

        _openLoaders.Clear();
        _nextWrite.Clear();
    }

    private void OnIdCardInserted(EntInsertedIntoContainerMessage args)
    {
        UpdateForIdCardChange(args.Container);
    }

    private void OnIdCardRemoved(EntRemovedFromContainerMessage args)
    {
        UpdateForIdCardChange(args.Container);
    }

    private void UpdateForIdCardChange(BaseContainer container)
    {
        if (container.ID != PdaComponent.PdaIdSlotId)
            return;

        var loader = container.Owner;
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) ||
            loaderComp.ActiveProgram is not { } program ||
            !HasComp<InternetCartridgeComponent>(program))
        {
            return;
        }

        // The PDA's cached ContainedId is updated by its own handler. Defer one tick so the
        // refreshed state sees the new card.
        Timer.Spawn(0, () => SendState(loader));
    }

    #endregion

    #region Messages

    private void OnMessage(EntityUid uid, InternetCartridgeComponent component, CartridgeMessageEvent args)
    {
        switch (args)
        {
            case InternetCreatePostMessage create:
                CreatePost(create, component.Network);
                break;
            case InternetEditPostMessage edit:
                EditPost(edit, component.Network, component.Admin);
                break;
            case InternetDeletePostMessage delete:
                DeletePost(delete, component.Network, component.Admin);
                break;
            case InternetLikePostMessage like:
                LikePost(like, component.Network);
                break;
            case InternetCreateCommentMessage comment:
                CreateComment(comment, component.Network);
                break;
            case InternetLikeCommentMessage likeComment:
                LikeComment(likeComment, component.Network);
                break;
            case InternetSendChatMessage chat:
                SendChatMessage(chat, component.Network);
                break;
            case InternetSetAliasMessage alias:
                SetAlias(alias, component.Network);
                break;
            case InternetAdminDeleteUserPostsMessage purge:
                AdminDeleteUserPosts(purge, component.Network, component.Admin);
                break;
            case InternetRefreshMessage refresh:
                SendState(GetEntity(refresh.LoaderUid));
                break;
        }
    }

    private void CreatePost(InternetCreatePostMessage message, InternetNetwork network)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        if (!TryConsumeWrite(network, viewer.UserId))
            return;

        if (!Enum.IsDefined(typeof(InternetPostType), message.Type))
            return;

        var title = NormalizeTitle(message.Title);
        if (title.Length == 0)
        {
            Reject(viewer.Actor, "internet-popup-title-required");
            return;
        }

        if (title.Length > InternetDefaults.MaxTitleLength)
            title = title[..InternetDefaults.MaxTitleLength];

        var body = (message.Body ?? string.Empty).Trim();
        if (body.Length > InternetDefaults.MaxBodyLength)
            body = body[..InternetDefaults.MaxBodyLength];

        var state = GetState(network);
        InternetPost? parent = null;
        if (message.ParentId != 0)
        {
            if (!state.PostsById.TryGetValue(message.ParentId, out var foundParent))
                return;

            if (foundParent.Type != InternetPostType.Topic)
            {
                Reject(viewer.Actor, "internet-popup-invalid-parent");
                return;
            }

            // Only nested topics count towards the depth limit; posts and chats may live inside
            // the deepest allowed topic.
            if (message.Type == InternetPostType.Topic &&
                GetDepth(state, foundParent) + 1 > InternetDefaults.MaxTopicDepth)
            {
                Reject(viewer.Actor, "internet-popup-too-deep");
                return;
            }

            parent = foundParent;
        }

        if (state.Posts.Count >= InternetDefaults.MaxPosts)
        {
            Reject(viewer.Actor, "internet-popup-limit");
            return;
        }

        if (!TryGetPostingIdentity(viewer, network, out var authorName))
            return;

        if (!TryPrepareImage(state, viewer.Actor, message.ImageData, 0, out var imageData))
            return;

        var post = new InternetPost
        {
            Id = state.NextPostId++,
            Type = message.Type,
            ParentId = parent?.Id ?? 0,
            Title = title,
            Body = body,
            ImageData = imageData,
            AuthorName = authorName,
            AuthorCKey = viewer.CKey,
            AuthorUserId = viewer.UserId,
            AuthorUserName = viewer.CKey,
            AuthorCard = viewer.CardUid,
            CreatedAt = Now(),
        };

        state.TotalImageBytes += imageData?.Length ?? 0;
        state.Posts.Insert(0, post);
        state.PostsById[post.Id] = post;

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} created {network} {post.Type} '{title}' ({authorName})");

        Broadcast();
    }

    private void EditPost(InternetEditPostMessage message, InternetNetwork network, bool adminMode)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post))
            return;

        var adminBypass = adminMode && viewer.IsAdmin;
        if (!adminBypass && !CanModify(post, network, viewer))
        {
            Reject(viewer.Actor, "internet-popup-not-author");
            return;
        }

        if (!TryConsumeWrite(network, viewer.UserId))
            return;

        var title = NormalizeTitle(message.Title);
        if (title.Length == 0)
        {
            Reject(viewer.Actor, "internet-popup-title-required");
            return;
        }

        if (title.Length > InternetDefaults.MaxTitleLength)
            title = title[..InternetDefaults.MaxTitleLength];

        var body = (message.Body ?? string.Empty).Trim();
        if (body.Length > InternetDefaults.MaxBodyLength)
            body = body[..InternetDefaults.MaxBodyLength];

        var oldImageLength = post.ImageData?.Length ?? 0;
        if (!TryPrepareImage(state, viewer.Actor, message.ImageData, oldImageLength, out var imageData))
            return;

        post.Title = title;
        post.Body = body;
        post.ImageData = imageData;
        post.EditedAt = Now();
        state.TotalImageBytes += (imageData?.Length ?? 0) - oldImageLength;

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} edited {network} {post.Type} '{title}' ({post.AuthorName})");

        Broadcast();
    }

    private void DeletePost(InternetDeletePostMessage message, InternetNetwork network, bool adminMode)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post))
            return;

        var adminBypass = adminMode && viewer.IsAdmin;
        if (!adminBypass && !CanModify(post, network, viewer))
        {
            Reject(viewer.Actor, "internet-popup-not-author");
            return;
        }

        if (!TryConsumeWrite(network, viewer.UserId))
            return;

        RemovePostSubtree(state, post);

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} deleted {network} {post.Type} '{post.Title}' ({post.AuthorName})");

        Broadcast();
    }

    private void LikePost(InternetLikePostMessage message, InternetNetwork network)
    {
        if (!TryGetSessionUser(message.Actor, out var userId))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post))
            return;

        ToggleLike(post.LikedBy, userId);
        Broadcast();
    }

    private void CreateComment(InternetCreateCommentMessage message, InternetNetwork network)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post))
            return;

        if (post.Type != InternetPostType.Post)
            return;

        if (!TryConsumeWrite(network, viewer.UserId))
            return;

        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            Reject(viewer.Actor, "internet-popup-message-required");
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
                Reject(viewer.Actor, "internet-popup-comment-too-deep");
                return;
            }
        }

        if (post.Comments.Count >= InternetDefaults.MaxCommentsPerPost)
        {
            Reject(viewer.Actor, "internet-popup-limit");
            return;
        }

        if (!TryGetPostingIdentity(viewer, network, out var authorName))
            return;

        post.Comments.Add(new InternetComment
        {
            Id = state.NextCommentId++,
            ParentId = message.ParentCommentId,
            AuthorName = authorName,
            AuthorCKey = viewer.CKey,
            AuthorUserId = viewer.UserId,
            Text = text,
            CreatedAt = Now(),
        });

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} commented on {network} post '{post.Title}' ({authorName})");

        Broadcast();
    }

    private void LikeComment(InternetLikeCommentMessage message, InternetNetwork network)
    {
        if (!TryGetSessionUser(message.Actor, out var userId))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post))
            return;

        var comment = post.Comments.FirstOrDefault(entry => entry.Id == message.CommentId);
        if (comment == null)
            return;

        ToggleLike(comment.LikedBy, userId);
        Broadcast();
    }

    private void SendChatMessage(InternetSendChatMessage message, InternetNetwork network)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        var state = GetState(network);
        if (!state.PostsById.TryGetValue(message.PostId, out var post) || post.Type != InternetPostType.Chat)
            return;

        if (!TryConsumeWrite(network, viewer.UserId))
            return;

        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length > InternetDefaults.MaxChatMessageLength)
            text = text[..InternetDefaults.MaxChatMessageLength];

        if (!TryPrepareImage(state, viewer.Actor, message.ImageData, 0, out var imageData))
            return;

        if (text.Length == 0 && imageData == null)
        {
            Reject(viewer.Actor, "internet-popup-message-required");
            return;
        }

        if (post.Messages.Count >= InternetDefaults.MaxMessagesPerChat)
        {
            Reject(viewer.Actor, "internet-popup-limit");
            return;
        }

        if (!TryGetPostingIdentity(viewer, network, out var authorName))
            return;

        state.TotalImageBytes += imageData?.Length ?? 0;
        post.Messages.Add(new InternetChatMessage
        {
            Id = state.NextMessageId++,
            AuthorName = authorName,
            AuthorCKey = viewer.CKey,
            AuthorUserId = viewer.UserId,
            Text = text,
            ImageData = imageData,
            CreatedAt = Now(),
        });

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} wrote in {network} chat '{post.Title}' ({authorName})");

        Broadcast();
    }

    private void SetAlias(InternetSetAliasMessage message, InternetNetwork network)
    {
        if (network != InternetNetwork.Darknet)
            return;

        if (!TryGetViewerContext(message, out var viewer))
            return;

        if (viewer.CardUid == EntityUid.Invalid)
        {
            Reject(viewer.Actor, "internet-popup-no-id");
            return;
        }

        var alias = NormalizeAlias(message.Alias);
        if (alias.Length == 0)
        {
            Reject(viewer.Actor, "darknet-popup-alias-required");
            return;
        }

        if (alias.Length > InternetDefaults.MaxAliasLength)
            alias = alias[..InternetDefaults.MaxAliasLength];

        var aliasComponent = EnsureComp<DarknetAliasComponent>(viewer.CardUid);
        aliasComponent.Alias = alias;

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(viewer.Actor):user} set Darknet alias '{alias}'");

        SendState(viewer.Loader);
    }

    private void AdminDeleteUserPosts(InternetAdminDeleteUserPostsMessage message, InternetNetwork network, bool adminMode)
    {
        if (!TryGetViewerContext(message, out var viewer))
            return;

        if (!adminMode || !viewer.IsAdmin)
        {
            Reject(viewer.Actor, "internet-popup-admin-only");
            return;
        }

        if (message.TargetUserId.UserId == Guid.Empty)
            return;

        var state = GetState(network);
        PurgeUserContent(state, message.TargetUserId);

        _adminLogger.Add(LogType.PdaInteract, LogImpact.Medium,
            $"{ToPrettyString(viewer.Actor):user} deleted all {network} posts by {message.TargetUserId}");

        Broadcast();
    }

    #endregion

    #region Identity

    private sealed class ViewerContext
    {
        public EntityUid Actor;
        public EntityUid Loader;
        public NetUserId UserId;
        public string CKey = string.Empty;
        public string CharacterName = string.Empty;
        public EntityUid CardUid;
        public string? IdCardName;
        public string? DarknetAlias;
        public bool IsAdmin;
    }

    private bool TryGetViewerContext(CartridgeMessageEvent message, out ViewerContext viewer)
    {
        viewer = new ViewerContext();
        var actor = message.Actor;
        var loader = GetEntity(message.LoaderUid);
        var built = BuildViewerContext(actor, loader);
        if (built == null)
            return false;

        viewer = built;
        return true;
    }

    private ViewerContext? BuildViewerContext(EntityUid actor, EntityUid loader)
    {
        if (!actor.Valid || TerminatingOrDeleted(actor))
            return null;

        if (!loader.Valid || TerminatingOrDeleted(loader))
            return null;

        if (!_player.TryGetSessionByEntity(actor, out var session))
            return null;

        var viewer = new ViewerContext
        {
            Actor = actor,
            Loader = loader,
            UserId = session.UserId,
            CKey = session.Name,
            CharacterName = Name(actor),
            IsAdmin = _admin.IsAdmin(session),
        };

        if (_idCards.TryGetIdCard(loader, out var card))
        {
            viewer.CardUid = card.Owner;
            viewer.IdCardName = card.Comp.FullName;
            viewer.DarknetAlias = GetDarknetAlias(card.Owner);
        }

        return viewer;
    }

    private ViewerContext GetViewerForLoader(EntityUid loader, CartridgeLoaderComponent loaderComp)
    {
        var actor = _ui.GetActors(loader, loaderComp.UiKey).FirstOrDefault();
        if (actor.Valid && BuildViewerContext(actor, loader) is { } viewer)
            return viewer;

        return new ViewerContext { Loader = loader };
    }

    private bool TryConsumeWrite(InternetNetwork network, NetUserId userId)
    {
        var key = (network, userId);
        var now = _timing.CurTime;
        if (_nextWrite.TryGetValue(key, out var next) && now < next)
            return false;

        _nextWrite[key] = now + WriteCooldown;
        return true;
    }

    private bool TryGetSessionUser(EntityUid actor, out NetUserId userId)
    {
        userId = default;
        if (!actor.Valid || TerminatingOrDeleted(actor))
            return false;

        if (!_player.TryGetSessionByEntity(actor, out var session))
            return false;

        userId = session.UserId;
        return true;
    }

    private bool TryGetPostingIdentity(ViewerContext viewer, InternetNetwork network, out string authorName)
    {
        authorName = string.Empty;

        if (viewer.CardUid == EntityUid.Invalid)
        {
            Reject(viewer.Actor, "internet-popup-no-id");
            return false;
        }

        if (network == InternetNetwork.Internet)
        {
            if (string.IsNullOrWhiteSpace(viewer.IdCardName))
            {
                Reject(viewer.Actor, "internet-popup-no-id");
                return false;
            }

            if (viewer.IdCardName != viewer.CharacterName)
            {
                Reject(viewer.Actor, "internet-popup-id-mismatch");
                return false;
            }

            authorName = viewer.CharacterName;
            return true;
        }

        if (string.IsNullOrWhiteSpace(viewer.DarknetAlias))
        {
            Reject(viewer.Actor, "darknet-popup-no-alias");
            return false;
        }

        authorName = viewer.DarknetAlias;
        return true;
    }

    private static bool CanModify(InternetPost post, InternetNetwork network, ViewerContext viewer)
    {
        if (network == InternetNetwork.Darknet)
            return viewer.CardUid != EntityUid.Invalid && post.AuthorCard == viewer.CardUid;

        return viewer.CardUid != EntityUid.Invalid
               && post.AuthorUserId == viewer.UserId
               && post.AuthorUserName == viewer.CKey
               && post.AuthorName == viewer.CharacterName
               && post.AuthorName == viewer.IdCardName;
    }

    private string? GetDarknetAlias(EntityUid card)
    {
        // The component is server-only; the alias is transferred with the card.
        if (!TryComp<DarknetAliasComponent>(card, out var alias) || string.IsNullOrWhiteSpace(alias.Alias))
            return null;

        return alias.Alias;
    }

    #endregion

    #region Validation

    private bool TryPrepareImage(NetworkState state, EntityUid actor, byte[]? data, int replacedBytes, out byte[]? imageData)
    {
        imageData = null;

        if (data == null || data.Length == 0)
            return true;

        if (data.Length > InternetDefaults.MaxImageBytes || !IsValidPng(data))
        {
            Reject(actor, "internet-popup-image-invalid");
            return false;
        }

        if (state.TotalImageBytes - replacedBytes + data.Length > InternetDefaults.MaxTotalImageBytes)
        {
            Reject(actor, "internet-popup-image-budget");
            return false;
        }

        imageData = data;
        return true;
    }

    /// <summary>
    ///     The client always re-encodes uploads as PNG. Checking the signature and the IHDR
    ///     dimensions here keeps malformed or decompression-bomb images out of the shared feed.
    /// </summary>
    private static bool IsValidPng(byte[] data)
    {
        if (data.Length < 24)
            return false;

        if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47 ||
            data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
        {
            return false;
        }

        var width = (uint)(data[16] << 24 | data[17] << 16 | data[18] << 8 | data[19]);
        var height = (uint)(data[20] << 24 | data[21] << 16 | data[22] << 8 | data[23]);

        return width > 0 &&
               height > 0 &&
               width <= InternetDefaults.MaxImageDimension &&
               height <= InternetDefaults.MaxImageDimension;
    }

    private static string NormalizeTitle(string? title)
    {
        return (title ?? string.Empty).Trim().Replace('\n', ' ').Replace('\r', ' ');
    }

    private static string NormalizeAlias(string? alias)
    {
        return (alias ?? string.Empty).Trim().Replace('\n', ' ').Replace('\r', ' ');
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

    private NetworkState GetState(InternetNetwork network)
    {
        return _networks[network];
    }

    private int GetDepth(NetworkState state, InternetPost post)
    {
        var depth = 1;
        var current = post;
        var guard = 0;

        while (current.ParentId != 0 && guard++ < 100)
        {
            if (!state.PostsById.TryGetValue(current.ParentId, out var parent))
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

    private void Broadcast()
    {
        foreach (var loader in _openLoaders.ToArray())
        {
            if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) ||
                loaderComp.ActiveProgram is not { } programUid ||
                !TryComp<InternetCartridgeComponent>(programUid, out var program))
            {
                _openLoaders.Remove(loader);
                continue;
            }

            SendState(loader, loaderComp, program);
        }
    }

    private void SendState(EntityUid loader, CartridgeLoaderComponent? loaderComp = null, InternetCartridgeComponent? program = null)
    {
        if (!Resolve(loader, ref loaderComp))
            return;

        if (program == null)
        {
            if (loaderComp.ActiveProgram is not { } programUid ||
                !TryComp<InternetCartridgeComponent>(programUid, out var activeProgram))
            {
                return;
            }

            program = activeProgram;
        }

        var state = GetState(program.Network);
        var hasCard = _idCards.TryGetIdCard(loader, out var card);
        var hasIdCard = hasCard && !string.IsNullOrWhiteSpace(card.Comp.FullName);
        var idCardName = hasIdCard ? card.Comp.FullName : null;
        var alias = program.Network == InternetNetwork.Darknet && hasCard
            ? GetDarknetAlias(card.Owner)
            : null;
        var viewer = GetViewerForLoader(loader, loaderComp);
        var adminView = program.Admin && viewer.IsAdmin;
        var snapshot = CreateSnapshot(state, program.Network, viewer, adminView, adminView);

        var uiState = new InternetUiState(
            snapshot,
            hasIdCard,
            idCardName,
            program.Network,
            program.Admin,
            viewer.IsAdmin,
            alias);

        _cartridge.UpdateCartridgeUiState(loader, uiState);
    }

    private List<InternetPostInfo> CreateSnapshot(
        NetworkState state,
        InternetNetwork network,
        ViewerContext viewer,
        bool includeCKey,
        bool adminBypass)
    {
        var snapshot = new List<InternetPostInfo>(state.Posts.Count);

        foreach (var post in state.Posts)
        {
            var info = new InternetPostInfo
            {
                Id = post.Id,
                Type = post.Type,
                ParentId = post.ParentId,
                Title = post.Title,
                Body = post.Body,
                ImageData = post.ImageData,
                AuthorName = post.AuthorName,
                AuthorCKey = includeCKey ? post.AuthorCKey : null,
                AuthorUserId = post.AuthorUserId,
                CreatedAt = post.CreatedAt,
                EditedAt = post.EditedAt,
                CanEdit = adminBypass || CanModify(post, network, viewer),
            };

            info.LikedBy.AddRange(post.LikedBy);

            foreach (var comment in post.Comments.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id))
            {
                var commentInfo = new InternetCommentInfo
                {
                    Id = comment.Id,
                    ParentId = comment.ParentId,
                    AuthorName = comment.AuthorName,
                    AuthorCKey = includeCKey ? comment.AuthorCKey : null,
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
                    AuthorCKey = includeCKey ? message.AuthorCKey : null,
                    Text = message.Text,
                    ImageData = message.ImageData,
                    CreatedAt = message.CreatedAt,
                });
            }

            snapshot.Add(info);
        }

        return snapshot;
    }

    #endregion

    #region Deletion

    private static void RemovePostSubtree(NetworkState state, InternetPost root)
    {
        var toRemove = new List<InternetPost>();
        var stack = new Stack<InternetPost>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            toRemove.Add(current);

            foreach (var child in state.Posts.Where(post => post.ParentId == current.Id))
                stack.Push(child);
        }

        foreach (var post in toRemove)
        {
            state.TotalImageBytes -= GetPostImageBytes(post);
            state.Posts.Remove(post);
            state.PostsById.Remove(post.Id);
        }
    }

    private static int GetPostImageBytes(InternetPost post)
    {
        var total = post.ImageData?.Length ?? 0;

        foreach (var message in post.Messages)
            total += message.ImageData?.Length ?? 0;

        return total;
    }

    private static void PurgeUserContent(NetworkState state, NetUserId target)
    {
        var roots = state.Posts.Where(post => post.AuthorUserId == target).ToList();
        foreach (var root in roots)
        {
            if (!state.PostsById.ContainsKey(root.Id))
                continue;

            RemovePostSubtree(state, root);
        }

        foreach (var post in state.Posts)
        {
            var commentIds = new HashSet<int>();
            foreach (var comment in post.Comments.Where(comment => comment.AuthorUserId == target))
                CollectCommentSubtree(post, comment, commentIds);

            if (commentIds.Count > 0)
                post.Comments.RemoveAll(comment => commentIds.Contains(comment.Id));

            for (var i = post.Messages.Count - 1; i >= 0; i--)
            {
                var message = post.Messages[i];
                if (message.AuthorUserId != target)
                    continue;

                state.TotalImageBytes -= message.ImageData?.Length ?? 0;
                post.Messages.RemoveAt(i);
            }
        }
    }

    private static void CollectCommentSubtree(InternetPost post, InternetComment comment, HashSet<int> ids)
    {
        if (!ids.Add(comment.Id))
            return;

        foreach (var child in post.Comments.Where(entry => entry.ParentId == comment.Id))
            CollectCommentSubtree(post, child, ids);
    }

    #endregion

    #region Data

    private sealed class NetworkState
    {
        public readonly List<InternetPost> Posts = new();
        public readonly Dictionary<int, InternetPost> PostsById = new();
        public int NextPostId = 1;
        public int NextCommentId = 1;
        public int NextMessageId = 1;
        public int TotalImageBytes;

        public void Reset()
        {
            Posts.Clear();
            PostsById.Clear();
            NextPostId = 1;
            NextCommentId = 1;
            NextMessageId = 1;
            TotalImageBytes = 0;
        }
    }

    private sealed class InternetPost
    {
        public int Id;
        public InternetPostType Type;
        public int ParentId;
        public string Title = string.Empty;
        public string Body = string.Empty;
        public byte[]? ImageData;
        public string AuthorName = string.Empty;
        public string AuthorCKey = string.Empty;
        public NetUserId AuthorUserId;
        public string AuthorUserName = string.Empty;
        public EntityUid AuthorCard;
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
        public string AuthorCKey = string.Empty;
        public NetUserId AuthorUserId;
        public string Text = string.Empty;
        public long CreatedAt;
        public readonly HashSet<NetUserId> LikedBy = new();
    }

    private sealed class InternetChatMessage
    {
        public int Id;
        public string AuthorName = string.Empty;
        public string AuthorCKey = string.Empty;
        public NetUserId AuthorUserId;
        public string Text = string.Empty;
        public byte[]? ImageData;
        public long CreatedAt;
    }

    #endregion
}
