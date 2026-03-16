using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace STS2Trade.Messages;

public struct GiveGoldMessage : INetMessage, IPacketSerializable
{
    public ulong targetPlayerId;
    public int amount;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(targetPlayerId);
        writer.WriteInt(amount);
    }

    public void Deserialize(PacketReader reader)
    {
        targetPlayerId = reader.ReadULong();
        amount = reader.ReadInt();
    }

    public override string ToString()
    {
        return $"GiveGoldMessage(target={targetPlayerId}, amount={amount})";
    }
}
