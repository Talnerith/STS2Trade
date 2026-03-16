using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using STS2Trade.UI;

namespace STS2Trade.Patches;

/// <summary>
/// Adds "Give Gold" buttons below non-local player characters in merchant rooms.
/// </summary>
[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
public static class MerchantRoomGoldButtonPatch
{
    private static readonly AccessTools.FieldRef<NMerchantRoom, List<Player>> PlayersRef =
        AccessTools.FieldRefAccess<NMerchantRoom, List<Player>>("_players");

    private static readonly AccessTools.FieldRef<NMerchantRoom, List<NMerchantCharacter>> PlayerVisualsRef =
        AccessTools.FieldRefAccess<NMerchantRoom, List<NMerchantCharacter>>("_playerVisuals");

    [HarmonyPostfix]
    public static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            ShopPatchHelper.EnsureGoldGiftSync();

            var players = PlayersRef(__instance);
            var visuals = PlayerVisualsRef(__instance);
            if (players == null || visuals == null) return;

            // _players[0] is always the local player (reordered in AfterRoomIsLoaded)
            // Skip single-player
            if (players.Count <= 1) return;

            for (int k = 1; k < players.Count && k < visuals.Count; k++)
            {
                var player = players[k];
                var character = visuals[k];
                if (player == null || character == null) continue;

                var button = NGiveGoldButton.Create(player.NetId);
                character.AddChild(button);
                // Position below the character sprite, centered
                button.Position = new Vector2(-60, 80);

                MainFile.Logger.Info($"Added Give Gold button for player {player.NetId} at merchant");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MerchantRoomGoldButtonPatch failed: {e}");
        }
    }
}

/// <summary>
/// Adds "Give Gold" buttons below non-local player characters in fake merchant events.
/// </summary>
[HarmonyPatch(typeof(NFakeMerchant), "AfterRoomIsLoaded")]
public static class FakeMerchantGoldButtonPatch
{
    private static readonly AccessTools.FieldRef<NFakeMerchant, List<Player>> PlayersRef =
        AccessTools.FieldRefAccess<NFakeMerchant, List<Player>>("_players");

    private static readonly AccessTools.FieldRef<NFakeMerchant, Control> CharContainerRef =
        AccessTools.FieldRefAccess<NFakeMerchant, Control>("_characterContainer");

    [HarmonyPostfix]
    public static void Postfix(NFakeMerchant __instance)
    {
        try
        {
            ShopPatchHelper.EnsureGoldGiftSync();

            var players = PlayersRef(__instance);
            var container = CharContainerRef(__instance);
            if (players == null || container == null) return;
            if (players.Count <= 1) return;

            // NFakeMerchant adds NCreatureVisuals to _characterContainer using MoveChild(0),
            // which reverses the visual order in the scene tree. The last player added
            // becomes child index 0. We need to map children back to players.
            //
            // Players are iterated in order [0..N-1] and each is inserted at index 0,
            // so scene tree order is reversed: child 0 = last player, child N-1 = first player.
            // _players[0] = local player = child (childCount - 1)
            // _players[k] = child (childCount - 1 - k)

            int childCount = container.GetChildCount();
            for (int k = 1; k < players.Count; k++)
            {
                int childIdx = childCount - 1 - k;
                if (childIdx < 0 || childIdx >= childCount) continue;

                var characterNode = container.GetChild(childIdx);
                if (characterNode == null) continue;

                var player = players[k];
                if (player == null) continue;

                var button = NGiveGoldButton.Create(player.NetId);
                characterNode.AddChild(button);
                // NCreatureVisuals origin is higher than NMerchantCharacter, so use smaller Y offset
                button.Position = new Vector2(-60, 30);

                MainFile.Logger.Info($"Added Give Gold button for player {player.NetId} at fake merchant");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"FakeMerchantGoldButtonPatch failed: {e}");
        }
    }
}

internal static class ShopPatchHelper
{
    internal static void EnsureGoldGiftSync()
    {
        var netService = RunManager.Instance?.NetService;
        if (netService == null) return;

        var existing = GoldGiftSynchronizer.Instance;
        if (existing != null)
        {
            if (existing.IsUsingService(netService)) return;
            existing.Dispose();
        }

        var runState = RunManager.Instance!.DebugOnlyGetState();
        if (runState == null || runState.Players.Count <= 1) return;

        var localId = LocalContext.NetId;
        if (!localId.HasValue) return;

        new GoldGiftSynchronizer(netService, runState, localId.Value);
    }
}
