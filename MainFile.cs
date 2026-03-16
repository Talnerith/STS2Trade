using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Trade;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    private const string ModId = "STS2Trade";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Logger.Info("STS2Trade: Campfire Trading mod initializing...");

        ModConfigRegistry.Register(ModId, new TradeConfig());

        Harmony harmony = new(ModId);
        harmony.PatchAll();

        Logger.Info("STS2Trade: Harmony patches applied.");

#if DEBUG
        DebugAutoTest.TryStart();
#endif
    }
}
