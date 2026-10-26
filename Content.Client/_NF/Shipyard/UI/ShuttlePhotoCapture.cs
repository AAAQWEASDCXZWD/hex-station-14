using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;

namespace Content.Client._NF.Shipyard.UI;

/// <summary>
///     Renders a shuttle grid through a hidden <see cref="ScalingViewport"/> and returns it as PNG bytes.
/// </summary>
/// <remarks>
///     The server force-sends the shuttle to this client before asking for the photo, so the grid and its
///     entities may show up a few ticks later. The capture therefore retries until the shuttle is loaded.
/// </remarks>
public sealed class ShuttlePhotoCapture
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(1.5);
    private const int MaxAttempts = 12;

    private readonly ScalingViewport _viewport;
    private readonly IEntityManager _entity;
    private readonly IEntitySystemManager _sysMan;
    private readonly ISawmill _sawmill;
    private readonly Queue<(NetEntity Uid, Action<byte[]?> Callback)> _queue = new();

    private bool _busy;
    private bool _cancelled;
    private int _attempts;

    public ShuttlePhotoCapture(ScalingViewport viewport, IEntityManager entity, IEntitySystemManager sysMan, ISawmill sawmill)
    {
        _viewport = viewport;
        _entity = entity;
        _sysMan = sysMan;
        _sawmill = sawmill;
        _viewport.ViewportSize = new Vector2i(160, 120);
    }

    /// <summary>
    ///     A capture is currently in flight.
    /// </summary>
    public bool IsBusy => _busy;

    /// <summary>
    ///     Stops any pending captures, e.g. when the window is closed.
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
        _busy = false;
        _queue.Clear();
        _viewport.Eye = null;
    }

    /// <summary>
    ///     Captures the given shuttle once the client has it loaded and calls back with PNG bytes
    ///     (or <c>null</c> if the capture failed). Requests are processed one at a time.
    /// </summary>
    public void Capture(NetEntity netUid, Action<byte[]?> callback)
    {
        if (_cancelled)
            return;

        if (_busy)
        {
            _queue.Enqueue((netUid, callback));
            return;
        }

        _busy = true;
        _attempts = 0;
        TryCapture(netUid, callback);
    }

    private void TryCapture(NetEntity netUid, Action<byte[]?> callback)
    {
        if (_cancelled)
            return;

        if (!_entity.TryGetEntity(netUid, out var shuttleUid) ||
            !_entity.TryGetComponent<TransformComponent>(shuttleUid, out var xform) ||
            !_entity.TryGetComponent<MapGridComponent>(shuttleUid, out var mapGrid) ||
            xform.MapID == MapId.Nullspace)
        {
            if (++_attempts > MaxAttempts)
            {
                Complete(callback, null);
                return;
            }

            Timer.Spawn(RetryDelay, () => TryCapture(netUid, callback));
            return;
        }

        var transform = _sysMan.GetEntitySystem<TransformSystem>();

        var localBounds = mapGrid.LocalAABB;
        var localCenter = localBounds.Center;
        var (worldPos, worldRot) = transform.GetWorldPositionRotation(xform);
        var centerPos = worldPos + worldRot.RotateVec(localCenter);

        var widthMeters = Math.Max(2f, localBounds.Width);
        var heightMeters = Math.Max(2f, localBounds.Height);

        // Fill ~85% of the frame with the ship, leaving a small margin.
        var zoomX = (widthMeters * 32f) / (160f * 0.85f);
        var zoomY = (heightMeters * 32f) / (120f * 0.85f);
        var zoom = Math.Clamp(Math.Max(zoomX, zoomY), 0.2f, 15f);

        var eye = new FixedEye
        {
            Position = new MapCoordinates(centerPos, xform.MapID),
            Rotation = worldRot,
            Zoom = new Vector2(zoom, zoom),
            DrawFov = false,
            DrawLight = false
        };

        _viewport.Eye = eye;

        var completed = false;
        Timer.Spawn(CaptureTimeout, () =>
        {
            if (completed)
                return;
            completed = true;
            _viewport.Eye = null;
            Complete(callback, null);
        });

        _viewport.Screenshot(image =>
        {
            if (completed)
                return;
            completed = true;

            byte[]? photoBytes = null;
            try
            {
                using var stream = new MemoryStream();
                image.SaveAsPng(stream);
                photoBytes = stream.ToArray();
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to encode shuttle screenshot to PNG: {ex}");
            }

            // Leave the screen's draw call before touching the UI or sending messages.
            Timer.Spawn(0, () =>
            {
                _viewport.Eye = null;
                Complete(callback, photoBytes);
            });
        });
    }

    private void Complete(Action<byte[]?> callback, byte[]? photoBytes)
    {
        _busy = false;

        if (!_cancelled)
            callback(photoBytes);

        if (_cancelled || _queue.Count == 0)
            return;

        var (uid, nextCallback) = _queue.Dequeue();
        _busy = true;
        _attempts = 0;
        TryCapture(uid, nextCallback);
    }
}

