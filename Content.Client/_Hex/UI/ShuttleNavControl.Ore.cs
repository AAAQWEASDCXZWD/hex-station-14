using System.Numerics;
using Content.Client._Hex.Mining;
using Content.Shared._Hex.Mining;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    /// <summary>
    /// Малює знайдені поклади руди на астероїдах пушистими сяючими точками в колір руди.
    /// </summary>
    private void DrawOreOverlay(DrawingHandleScreen handle, Matrix3x2 worldToShuttle, Matrix3x2 shuttleToView)
    {
        if (_consoleEntity is not { } consoleUid)
            return;

        var scannerSys = EntManager.System<ShuttleOreScannerClientSystem>();
        scannerSys.RequestOreBlips(consoleUid);

        var cachedBlips = scannerSys.CachedBlips;
        if (cachedBlips.Count == 0)
            return;

        var viewBounds = new Box2(-10f, -10f, Size.X + 10f, Size.Y + 10f);

        foreach (var (netGrid, blips) in cachedBlips)
        {
            if (!EntManager.TryGetEntity(netGrid, out var gridUid) ||
                !EntManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
                continue;

            var gridToWorld = _transform.GetWorldMatrix(gridUid.Value);
            var gridToView = gridToWorld * worldToShuttle * shuttleToView;
            var tileSize = grid.TileSize;

            var baseRadius = MathF.Max(2.5f, tileSize * MinimapScale * 0.45f);

            foreach (var blip in blips)
            {
                var localTilePos = new Vector2(blip.X + 0.5f, blip.Y + 0.5f) * tileSize;
                var screenPos = Vector2.Transform(localTilePos, gridToView);

                if (!viewBounds.Contains(screenPos))
                    continue;

                var oreColor = HexOreColors.GetColor(blip.Type);

                // 1. Пушисте напівпрозоре зовнішнє сяйво (halo)
                handle.DrawCircle(screenPos, baseRadius * 1.8f, oreColor.WithAlpha(0.22f));

                // 2. Насичене тіло сяйва
                handle.DrawCircle(screenPos, baseRadius * 1.15f, oreColor.WithAlpha(0.60f));

                // 3. Яскраве неонове ядро з білуватим відтінком
                var coreColor = Color.InterpolateBetween(oreColor, Color.White, 0.45f).WithAlpha(0.95f);
                handle.DrawCircle(screenPos, baseRadius * 0.65f, coreColor);
            }
        }
    }
}
