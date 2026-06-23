using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace STS2Trade.Messages;

public struct TradeOfferMessage : INetMessage, IPacketSerializable
{
    public int cardCount;
    public int[] cardDeckIndices;

    public int potionCount;
    public int[] potionSlotIndices;

    public int relicCount;
    public int[] relicIndices;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(cardCount, 8);
        for (int i = 0; i < cardCount; i++)
        {
            writer.WriteInt(cardDeckIndices[i]);
        }

        writer.WriteInt(potionCount, 8);
        for (int i = 0; i < potionCount; i++)
        {
            writer.WriteInt(potionSlotIndices[i], 8);
        }

        writer.WriteInt(relicCount, 8);
        for (int i = 0; i < relicCount; i++)
        {
            writer.WriteInt(relicIndices[i]);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        cardCount = reader.ReadInt(8);
        cardDeckIndices = new int[cardCount];
        for (int i = 0; i < cardCount; i++)
        {
            cardDeckIndices[i] = reader.ReadInt();
        }

        potionCount = reader.ReadInt(8);
        potionSlotIndices = new int[potionCount];
        for (int i = 0; i < potionCount; i++)
        {
            potionSlotIndices[i] = reader.ReadInt(8);
        }

        relicCount = reader.ReadInt(8);
        relicIndices = new int[relicCount];
        for (int i = 0; i < relicCount; i++)
        {
            relicIndices[i] = reader.ReadInt();
        }
    }

    public override string ToString()
    {
        return $"TradeOfferMessage(cards={cardCount}, potions={potionCount}, relics={relicCount})";
    }
}
