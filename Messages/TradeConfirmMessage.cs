using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace STS2Trade.Messages;

public struct TradeConfirmMessage : INetMessage, IPacketSerializable
{
    public bool confirmed;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(confirmed);
    }

    public void Deserialize(PacketReader reader)
    {
        confirmed = reader.ReadBool();
    }

    public override string ToString()
    {
        return $"TradeConfirmMessage(confirmed={confirmed})";
    }
}
