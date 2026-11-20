using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[Serializable, NetSerializable]
public enum FactoryPortType : byte
{
    Input,
    Output,
    Bidirectional,
}
