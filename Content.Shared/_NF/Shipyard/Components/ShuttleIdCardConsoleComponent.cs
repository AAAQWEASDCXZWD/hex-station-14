using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NF.Shipyard.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleIdCardConsoleComponent : Component
{
    public static string PrivilegedIdCardSlotId = "ShuttleIdCardConsole-privilegedId";
    public static string TargetIdCardSlotId = "ShuttleIdCardConsole-targetId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public ItemSlot TargetIdSlot = new();

    /// <summary>
    /// Available access levels that can be granted to shuttle crew.
    /// Includes all represented access levels in the game.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> AccessLevels = new()
    {
        "Shuttle",
        "Armory",
        "Atmospherics",
        "Bailiff",
        "Bar",
        "Brig",
        "Brigmedic",
        "Captain",
        "Cargo",
        "Chapel",
        "Chemistry",
        "ChiefEngineer",
        "ChiefMedicalOfficer",
        "Command",
        "Cryogenics",
        "Detective",
        "Engineering",
        "External",
        "Frontier",
        "HeadOfPersonnel",
        "HeadOfSecurity",
        "Hydroponics",
        "Janitor",
        "Kitchen",
        "Lawyer",
        "Mail",
        "Maintenance",
        "Medical",
        "Mercenary",
        "Paramedic",
        "Pilot",
        "Pirate",
        "Quartermaster",
        "Research",
        "ResearchDirector",
        "Salvage",
        "Security",
        "Sergeant",
        "Service",
        "StationTrafficController",
        "Theatre",
    };
}
