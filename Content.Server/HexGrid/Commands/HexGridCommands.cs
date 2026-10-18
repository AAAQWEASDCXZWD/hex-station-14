using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.HexGrid;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using HexMath = Content.Shared.HexGrid.HexGrid;

namespace Content.Server.HexGrid.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed class HexCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "hex";
    public string Description => "Shows the axial coordinate of the hex containing you.";
    public string Help => "Usage: hex";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryGetPlayerMapPosition(shell, _entityManager, out var position))
            return;

        var hex = HexMath.GetCoordinates(position.Position);
        var center = HexMath.GetCenter(hex);
        var standardZone = hex.DistanceFromOrigin <= HexMath.StandardHexRadius ? "так" : "ні";

        shell.WriteLine(
            $"Параметр         Значення\n" +
            $"q                {hex.Q}\n" +
            $"r                {hex.R}\n" +
            $"центр X          {center.X:0.00}\n" +
            $"центр Y          {center.Y:0.00}\n" +
            $"стандартна зона  {standardZone}");
    }

    internal static bool TryGetPlayerMapPosition(IConsoleShell shell, IEntityManager entityManager, out MapCoordinates position)
    {
        position = default;

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError("Команду може виконати лише гравець у грі.");
            return false;
        }

        position = entityManager.System<SharedTransformSystem>().GetMapCoordinates(player);
        return true;
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class HexSpawnCommand : IConsoleCommand
{
    private const string DebugPrototype = "ToolDebug";

    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "hexspawn";
    public string Description => "Spawns a debug tool at every vertex of your current standard hex.";
    public string Help => "Usage: hexspawn";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (!HexCommand.TryGetPlayerMapPosition(shell, _entityManager, out var playerPosition))
            return;

        var hex = HexMath.GetCoordinates(playerPosition.Position);
        if (hex.DistanceFromOrigin > HexMath.StandardHexRadius)
        {
            shell.WriteError("Гравець поза межами стандартних 7 гексів.");
            return;
        }

        foreach (var vertex in HexMath.GetVertices(hex))
        {
            _entityManager.SpawnEntity(DebugPrototype, new MapCoordinates(vertex, playerPosition.MapId));
        }

        shell.WriteLine($"Створено 6 предметів {DebugPrototype} у вершинах гекса q={hex.Q}, r={hex.R}.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class HexNameCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "hexname";
    public string Description => "Changes the displayed name of a hex for the current round.";
    public string Help => "Usage: hexname <q> <r> <name>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[0], out var q) || !int.TryParse(args[1], out var r))
        {
            shell.WriteError(Help);
            return;
        }

        var name = string.Join(' ', args[2..]).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            shell.WriteError(Help);
            return;
        }

        _entityManager.System<HexGridNameSystem>().SetName(new HexCoordinates(q, r), name);
        shell.WriteLine($"Hex q={q}, r={r} renamed to '{name}'.");
    }
}
