using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace STS2Trade.UI;

/// <summary>
/// Manages trade notification bubbles:
/// - On the TARGET's screen: shows a thought bubble with the trade icon from the requesting
///   player's rest site character (like when hovering a rest site option).
/// - On the SELECTOR's screen: the thought bubble is handled by the rest site character system
///   already via ShowHoveredRestSiteOption.
/// </summary>
public partial class TradeNotificationManager : Node
{
    private static TradeNotificationManager? _instance;
    public static TradeNotificationManager? Instance => _instance;

    // Track which characters currently have a trade thought bubble
    private readonly Dictionary<ulong, NThoughtBubbleVfx> _activeBubbles = new();

    public override void _Ready()
    {
        _instance = this;
    }

    public override void _ExitTree()
    {
        ClearAllBubbles();
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        var sync = TradeSynchronizer.Instance;
        if (sync == null) return;

        UpdateTradeRequestBubbles(sync);
    }

    private void UpdateTradeRequestBubbles(TradeSynchronizer sync)
    {
        var restSiteRoom = NRestSiteRoom.Instance;
        if (restSiteRoom == null)
        {
            ClearAllBubbles();
            return;
        }

        // Find which remote players are requesting to trade with us
        HashSet<ulong> requestingPlayers = new();

        if (sync.Phase == TradePhase.Idle || sync.Phase == TradePhase.SelectingPartner || sync.Phase == TradePhase.WaitingForPartner)
        {
            foreach (var kvp in sync.PendingRequests)
            {
                ulong fromPlayerId = kvp.Key;
                // Only show bubbles for other players requesting to trade with us
                if (sync.HasPendingRequestFrom(fromPlayerId))
                {
                    requestingPlayers.Add(fromPlayerId);
                }
            }
        }

        // Remove bubbles for players who are no longer requesting
        var toRemove = _activeBubbles.Keys.Where(id => !requestingPlayers.Contains(id)).ToList();
        foreach (var playerId in toRemove)
        {
            RemoveBubble(playerId);
        }

        // Add bubbles for new requesters
        foreach (var playerId in requestingPlayers)
        {
            if (!_activeBubbles.ContainsKey(playerId))
            {
                ShowTradeRequestBubble(playerId, restSiteRoom);
            }
        }
    }

    private void ShowTradeRequestBubble(ulong fromPlayerId, NRestSiteRoom restSiteRoom)
    {
        // Find the NRestSiteCharacter for the requesting player
        NRestSiteCharacter? character = FindCharacterForPlayer(fromPlayerId, restSiteRoom);
        if (character == null) return;

        // Match the game's DialogueSide logic based on character index
        int charIdx = character._characterIndex;
        bool isRightSide = charIdx == 0 || charIdx == 3;
        var side = isRightSide ? DialogueSide.Right : DialogueSide.Left;

        var bubble = NThoughtBubbleVfx.Create("Trade?", side, null);
        if (bubble == null) return;

        // Add to scene tree first, then position at the character's thought bubble anchor
        character.AddChildSafely(bubble);
        bubble.GlobalPosition = character.GetRestSiteOptionAnchor().GlobalPosition;

        _activeBubbles[fromPlayerId] = bubble;

        MainFile.Logger.Info($"Showing trade request bubble for player {fromPlayerId}");
    }

    private void RemoveBubble(ulong playerId)
    {
        if (_activeBubbles.TryGetValue(playerId, out var bubble))
        {
            if (GodotObject.IsInstanceValid(bubble))
            {
                TaskHelper.RunSafely(bubble.GoAway());
            }
            _activeBubbles.Remove(playerId);
        }
    }

    private void ClearAllBubbles()
    {
        foreach (var kvp in _activeBubbles)
        {
            if (GodotObject.IsInstanceValid(kvp.Value))
            {
                TaskHelper.RunSafely(kvp.Value.GoAway());
            }
        }
        _activeBubbles.Clear();
    }

    private NRestSiteCharacter? FindCharacterForPlayer(ulong playerId, NRestSiteRoom restSiteRoom)
    {
        // Use the game's characterAnims list for reliable character lookup
        foreach (var character in restSiteRoom.characterAnims)
        {
            if (character.Player?.NetId == playerId)
            {
                return character;
            }
        }
        return null;
    }
}
