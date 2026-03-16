using System.Reflection;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace STS2Trade;

public enum CardSlots { One = 1, Two = 2, Three = 3, Four = 4, Five = 5 }
public enum PotionSlots { One = 1, Two = 2, Three = 3 }
public enum RelicSlots { One = 1, Two = 2, Three = 3 }

public class TradeConfig : SimpleModConfig
{
    public static CardSlots MaxCardSlots { get; set; } = CardSlots.Three;
    public static PotionSlots MaxPotionSlots { get; set; } = PotionSlots.Three;
    public static RelicSlots MaxRelicSlots { get; set; } = RelicSlots.One;
    public static bool UnlimitedTrades { get; set; } = false;
    public static bool BlockObtainHookRelics { get; set; } = true;
    public static bool BlockQuestCards { get; set; } = true;

    public TradeConfig() { }

    public override void SetupConfigUI(Control optionContainer)
    {
        foreach (var child in optionContainer.GetChildren())
            child.Free();

        base.SetupConfigUI(optionContainer);
    }

    public static int MaxCardSlotsInt => (int)MaxCardSlots;
    public static int MaxPotionSlotsInt => (int)MaxPotionSlots;
    public static int MaxRelicSlotsInt => (int)MaxRelicSlots;

    /// <summary>
    /// Returns true if the given relic has an AfterObtained hook that does
    /// something beyond the base no-op (i.e., the method is overridden).
    /// </summary>
    public static bool HasObtainHook(RelicModel relic)
    {
        var method = relic.GetType().GetMethod("AfterObtained",
            BindingFlags.Instance | BindingFlags.Public);
        return method != null && method.DeclaringType != typeof(RelicModel);
    }
}
