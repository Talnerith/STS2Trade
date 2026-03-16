using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace STS2Trade.Messages;

public struct TradeTargetMessage : INetMessage, IPacketSerializable
{
    public bool hasTarget;
    public ulong targetPlayerId;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(hasTarget);
        if (hasTarget)
        {
            writer.WriteULong(targetPlayerId);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        hasTarget = reader.ReadBool();
        if (hasTarget)
        {
            targetPlayerId = reader.ReadULong();
        }
    }

    public override string ToString()
    {
        return hasTarget
            ? $"TradeTargetMessage(target={targetPlayerId})"
            : "TradeTargetMessage(deselect)";
    }
}
