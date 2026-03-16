using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2Trade.UI;

namespace STS2Trade.Patches;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
public static class AddTradeOptionPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player player, List<RestSiteOption> __result)
    {
        if (player.RunState.Players.Count <= 1)
            return;

        // Always add Trade for ALL players to keep option indices consistent across
        // machines, regardless of whether the player has already traded. The option's
        // UpdateIsEnabled() will disable it (gray it out) if CanTrade returns false.
        // This prevents desync when players have different UnlimitedTrades settings.
        __result.Add(new TradeRestSiteOption(player));
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldDisableRemainingRestSiteOptions))]
public static class PreventDisableAfterTradePatch
{
    [HarmonyPrefix]
    public static bool Prefix(IRunState runState, Player player, ref bool __result)
    {
        var sync = TradeSynchronizer.Instance;
        if (sync != null && sync.JustCompletedTrade.Contains(player.NetId))
        {
            sync.JustCompletedTrade.Remove(player.NetId);
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeginRestSite))]
public static class InitTradeSyncPatch
{
    [HarmonyPrefix]
    public static void Prefix(RestSiteSynchronizer __instance)
    {
        var netService = RunManager.Instance.NetService;
        if (netService == null) return;

        var existingSync = TradeSynchronizer.Instance;
        if (existingSync != null)
        {
            if (existingSync.IsUsingService(netService))
            {
                // Same session — just reset state for the new rest site
                existingSync.ResetForNewRestSite();
                return;
            }

            // Stale instance from a previous game session — dispose it so we
            // create a fresh one with the current (connected) net service.
            MainFile.Logger.Info("Disposing stale TradeSynchronizer from previous session");
            existingSync.Dispose();
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || runState.Players.Count <= 1) return;

        var localId = LocalContext.NetId;
        if (!localId.HasValue) return;

        new TradeSynchronizer(netService, runState, localId.Value);
        MainFile.Logger.Info($"Created TradeSynchronizer for player {localId.Value}");
    }
}

/// <summary>
/// Prevents the permanent "selected option" confirmation icon from appearing on
/// rest site characters after a trade. Without this, the confirmation replaces the
/// thought bubble system and blocks hover previews for all remaining options.
/// </summary>
[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.ShowSelectedRestSiteOption))]
public static class SkipTradeConfirmationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RestSiteOption option)
    {
        // Skip showing the permanent confirmation icon for trade options.
        // Trade doesn't consume the rest site action, so we want hover thought
        // bubbles to keep working after the trade completes.
        return option is not TradeRestSiteOption;
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
public static class AddNotificationManagerPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRestSiteRoom __instance)
    {
        if (__instance.Options.Count > 0)
        {
            var existing = __instance.GetNodeOrNull<TradeNotificationManager>("TradeNotificationManager");
            if (existing == null)
            {
                var manager = new TradeNotificationManager();
                manager.Name = "TradeNotificationManager";
                __instance.AddChild(manager);
            }
        }
    }
}
