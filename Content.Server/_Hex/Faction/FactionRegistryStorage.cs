using System;
using System.Collections.Generic;
using System.Text.Json;
using Content.Shared._Hex.Faction;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.Server._Hex.Faction;

/// <summary>
///     JSON persistence for the global faction registry.
/// </summary>
/// <remarks>
///     The registry is stored in the server user data directory so factions survive between rounds.
///     Character entities and terminal entities are not persisted here: character membership is stored
///     in <see cref="Content.Shared.Preferences.HumanoidCharacterProfile"/> and terminals re-register
///     themselves from their saved <see cref="FactionComputerComponent.FactionId"/> on startup.
/// </remarks>
public sealed class FactionRegistryStorage
{
    private static readonly ResPath StoragePath = new("/factions.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    private readonly IResourceManager _resources;
    private readonly ISawmill _sawmill;

    public FactionRegistryStorage(IResourceManager resources, ISawmill sawmill)
    {
        _resources = resources;
        _sawmill = sawmill;
    }

    public List<FactionRecord> Load()
    {
        try
        {
            if (!_resources.UserData.Exists(StoragePath))
                return new List<FactionRecord>();

            using var reader = _resources.UserData.OpenText(StoragePath);
            var records = JsonSerializer.Deserialize<List<FactionRecord>>(reader.ReadToEnd(), SerializerOptions);
            return records ?? new List<FactionRecord>();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to load the faction registry: {e}");
            return new List<FactionRecord>();
        }
    }

    public void Save(IEnumerable<FactionRecord> factions)
    {
        try
        {
            using var writer = _resources.UserData.OpenWriteText(StoragePath);
            writer.Write(JsonSerializer.Serialize(factions, SerializerOptions));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to save the faction registry: {e}");
        }
    }
}

/// <summary>
///     Serializable representation of a faction from the registry.
/// </summary>
public sealed class FactionRecord
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string ColorHex = FactionDefaults.DefaultColor;
    public string PhotoId = FactionDefaults.DefaultPhoto;

    public Guid LeaderUserId;
    public string LeaderCharacterName = string.Empty;

    public List<FactionMemberRecord> Members = new();
}

/// <summary>
///     Serializable representation of a character's faction membership.
/// </summary>
public sealed class FactionMemberRecord
{
    public Guid UserId;
    public string CharacterName = string.Empty;
    public FactionRole Role = FactionRole.Member;
}
