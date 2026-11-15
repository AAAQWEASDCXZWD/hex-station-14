using System.Numerics;
using Content.Shared._Hex.FireControl;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Fire-control overlay drawn on top of the gunnery radar. The main
/// <see cref="ShuttleNavControl.Draw"/> calls the hook below; keeping the state and
/// rendering here avoids threading gunnery data through the generic radar control.
/// </summary>
public sealed partial class ShuttleNavControl
{
    private FireControllableEntry[] _fireControlWeapons = Array.Empty<FireControllableEntry>();
    private readonly HashSet<NetEntity> _fireControlSelected = new();
    private bool _fireControlEnabled;

    public void SetFireControlWeapons(FireControllableEntry[] weapons)
    {
        _fireControlWeapons = weapons ?? Array.Empty<FireControllableEntry>();
        _fireControlEnabled = true;
    }

    public void SetFireControlSelection(IEnumerable<NetEntity> selected)
    {
        _fireControlSelected.Clear();

        foreach (var netEntity in selected)
            _fireControlSelected.Add(netEntity);
    }

    private void DrawFireControlOverlay(DrawingHandleScreen handle)
    {
        if (!_fireControlEnabled || _coordinates == null || _rotation == null)
            return;

        var xformQuery = EntManager.GetEntityQuery<TransformComponent>();
        if (!xformQuery.TryGetComponent(_coordinates.Value.EntityId, out var xform) ||
            xform.MapID == MapId.Nullspace)
        {
            return;
        }

        // Mirrors the matrix used by ShuttleNavControl.Draw so the lines land on
        // the same pixels as the radar blips.
        var posMatrix = Matrix3Helpers.CreateTransform(_coordinates.Value.Position, _rotation.Value);
        var ourEntRot = RotateWithEntity ? _transform.GetWorldRotation(xform) : _rotation.Value;
        var ourEntMatrix = Matrix3Helpers.CreateTransform(_transform.GetWorldPosition(xform), ourEntRot);
        var shuttleToWorld = Matrix3x2.Multiply(posMatrix, ourEntMatrix);
        if (!Matrix3x2.Invert(shuttleToWorld, out var worldToShuttle))
            return;

        var shuttleToView = Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) *
                            Matrix3x2.CreateTranslation(MidPoint);
        var worldToView = worldToShuttle * shuttleToView;

        var cursor = _lastMousePos;
        handle.DrawCircle(cursor, 5f, Color.Lime.WithAlpha(0.9f), false);
        handle.DrawLine(cursor - new Vector2(8f, 0f), cursor + new Vector2(8f, 0f), Color.Lime.WithAlpha(0.6f));
        handle.DrawLine(cursor - new Vector2(0f, 8f), cursor + new Vector2(0f, 8f), Color.Lime.WithAlpha(0.6f));

        foreach (var entry in _fireControlWeapons)
        {
            if (!_fireControlSelected.Contains(entry.NetEntity))
                continue;

            var weaponCoords = EntManager.GetCoordinates(entry.Coordinates);
            if (!weaponCoords.IsValid(EntManager))
                continue;

            var mapCoords = _transform.ToMapCoordinates(weaponCoords);
            if (mapCoords.MapId != xform.MapID)
                continue;

            var weaponWorld = mapCoords.Position;
            var weaponView = Vector2.Transform(weaponWorld, worldToView);

            handle.DrawLine(weaponView, cursor, Color.Cyan.WithAlpha(0.55f));
            handle.DrawCircle(weaponView, 3.5f, Color.Cyan);

            if (!string.IsNullOrEmpty(entry.Name))
                handle.DrawString(Font, weaponView + new Vector2(5f, -5f), entry.Name, UIScale * 0.7f, Color.Cyan);
        }
    }
}
