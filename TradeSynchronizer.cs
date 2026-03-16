using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2Trade.Messages;

namespace STS2Trade;

public class TradeSynchronizer : IDisposable
{
    private static TradeSynchronizer? _instance;
    public static TradeSynchronizer? Instance => _instance;

    private readonly INetGameService _netService;
    private readonly IPlayerCollection _playerCollection;
    private readonly ulong _localPlayerId;

    /// <summary>
    /// Returns true if this synchronizer is using the given net service.
    /// Used to detect stale instances from previous game sessions.
    /// </summary>
    public bool IsUsingService(INetGameService service) => ReferenceEquals(_netService, service);

    public TradePhase Phase { get; set; } = TradePhase.Idle;
    public TradeSession? ActiveSession { get; private set; }

    /// <summary>
    /// Tracks a trade between two OTHER players that this machine is not participating in.
    /// Needed for 3+ player games so all machines keep state in sync.
    /// Uses the same pattern as EventSynchronizer: each machine independently executes
    /// the same logic based on the synced choices (offers/confirms).
    /// Convention: LocalPlayerId = lower NetId, PartnerPlayerId = higher NetId.
    /// </summary>
    private TradeSession? _observedSession;

    public Dictionary<ulong, ulong> PendingRequests { get; } = new();
    public HashSet<ulong> PlayersWhoTraded { get; } = new();
    public HashSet<ulong> JustCompletedTrade { get; } = new();

    public event Action? TradeStateChanged;
    public void NotifyStateChanged() => TradeStateChanged?.Invoke();
    public event Action<ulong, ulong>? TradeRequestReceived;
    public event Action? TradeStarted;
    public event Action? OfferUpdated;
    public event Action? ConfirmChanged;
    public event Action? TradeCancelled;
    /// <summary>
    /// Fired when a trade completes. Parameters are the two player NetIds involved.
    /// Listeners must check whether they care about this specific trade.
    /// </summary>
    public event Action<ulong, ulong>? TradeCompleted;

    public TradeSynchronizer(INetGameService netService, IPlayerCollection playerCollection, ulong localPlayerId)
    {
        _netService = netService;
        _playerCollection = playerCollection;
        _localPlayerId = localPlayerId;

        MainFile.Logger.Info($"TradeSynchronizer: Registering message handlers on {netService.GetType().Name}, IsConnected={netService.IsConnected}");
        _netService.RegisterMessageHandler<TradeTargetMessage>(HandleTradeTarget);
        _netService.RegisterMessageHandler<TradeOfferMessage>(HandleTradeOffer);
        _netService.RegisterMessageHandler<TradeConfirmMessage>(HandleTradeConfirm);
        _netService.RegisterMessageHandler<TradeCancelMessage>(HandleTradeCancel);
        _netService.RegisterMessageHandler<TradeConfigMessage>(HandleTradeConfig);

        _instance = this;

        // Host broadcasts its config to all clients so everyone uses the same rules
        if (_netService is INetHostGameService)
        {
            BroadcastConfig();
        }
    }

    public void Dispose()
    {
        _netService.UnregisterMessageHandler<TradeTargetMessage>(HandleTradeTarget);
        _netService.UnregisterMessageHandler<TradeOfferMessage>(HandleTradeOffer);
        _netService.UnregisterMessageHandler<TradeConfirmMessage>(HandleTradeConfirm);
        _netService.UnregisterMessageHandler<TradeCancelMessage>(HandleTradeCancel);
        _netService.UnregisterMessageHandler<TradeConfigMessage>(HandleTradeConfig);

        if (_instance == this)
            _instance = null;
    }

    public void ResetForNewRestSite()
    {
        Phase = TradePhase.Idle;
        ActiveSession = null;
        _observedSession = null;
        PendingRequests.Clear();
        PlayersWhoTraded.Clear();
        JustCompletedTrade.Clear();
    }

    public bool CanTrade(ulong playerId)
    {
        if (TradeConfig.UnlimitedTrades) return true;
        return !PlayersWhoTraded.Contains(playerId);
    }

    public bool HasPendingRequestFrom(ulong fromPlayerId)
    {
        return PendingRequests.TryGetValue(fromPlayerId, out ulong target) && target == _localPlayerId;
    }

    public void SelectTarget(ulong targetPlayerId)
    {
        PendingRequests[_localPlayerId] = targetPlayerId;
        Phase = TradePhase.WaitingForPartner;

        MainFile.Logger.Info($"SelectTarget: Sending TradeTargetMessage to {targetPlayerId}, netService.IsConnected={_netService.IsConnected}");
        _netService.SendMessage(new TradeTargetMessage
        {
            hasTarget = true,
            targetPlayerId = targetPlayerId
        });

        CheckForMatch();
        TradeStateChanged?.Invoke();
    }

    public void DeselectTarget()
    {
        PendingRequests.Remove(_localPlayerId);
        Phase = TradePhase.Idle;

        _netService.SendMessage(new TradeTargetMessage
        {
            hasTarget = false,
            targetPlayerId = 0
        });

        TradeStateChanged?.Invoke();
    }

    public void UpdateLocalOffer(TradeOffer offer)
    {
        if (ActiveSession == null) return;

        // Snapshot the data first — offer may be the same object as ActiveSession.LocalOffer,
        // so Clear() would wipe it before AddRange could read from it.
        var cardIndices = offer.CardDeckIndices.ToArray();
        var potionIndices = offer.PotionSlotIndices.ToArray();
        var relicIndices = offer.RelicIndices.ToArray();

        ActiveSession.LocalOffer.Clear();
        ActiveSession.LocalOffer.CardDeckIndices.AddRange(cardIndices);
        ActiveSession.LocalOffer.PotionSlotIndices.AddRange(potionIndices);
        ActiveSession.LocalOffer.RelicIndices.AddRange(relicIndices);

        if (ActiveSession.PartnerConfirmed)
            ActiveSession.PartnerConfirmed = false;
        if (ActiveSession.LocalConfirmed)
            ActiveSession.LocalConfirmed = false;

        _netService.SendMessage(new TradeOfferMessage
        {
            cardCount = cardIndices.Length,
            cardDeckIndices = cardIndices,
            potionCount = potionIndices.Length,
            potionSlotIndices = potionIndices,
            relicCount = relicIndices.Length,
            relicIndices = relicIndices
        });

        OfferUpdated?.Invoke();
        TradeStateChanged?.Invoke();
    }

    public void SetLocalConfirmed(bool confirmed)
    {
        if (ActiveSession == null) return;

        ActiveSession.LocalConfirmed = confirmed;
        if (confirmed)
            ActiveSession.PartnerOfferWhenLocalConfirmed = ActiveSession.PartnerOffer.Clone();

        _netService.SendMessage(new TradeConfirmMessage { confirmed = confirmed });

        ConfirmChanged?.Invoke();

        if (ActiveSession.BothConfirmed)
            ExecuteTrade();

        TradeStateChanged?.Invoke();
    }

    public void CancelTrade()
    {
        MainFile.Logger.Info($"CancelTrade: Sending TradeCancelMessage. Phase={Phase}, ActiveSession={(ActiveSession != null ? $"Local={ActiveSession.LocalPlayerId},Partner={ActiveSession.PartnerPlayerId}" : "null")}");
        MainFile.Logger.Info($"CancelTrade: Stack trace: {Environment.StackTrace}");
        _netService.SendMessage(new TradeCancelMessage());

        CleanupTrade();
        TradeCancelled?.Invoke();
        TradeStateChanged?.Invoke();
    }

    // =========================================================================
    // Message Handlers
    // =========================================================================

    private void HandleTradeTarget(TradeTargetMessage message, ulong senderId)
    {
        MainFile.Logger.Info($"HandleTradeTarget: from={senderId}, hasTarget={message.hasTarget}, target={message.targetPlayerId}, localId={_localPlayerId}");
        if (senderId == _localPlayerId) return;

        if (message.hasTarget)
        {
            PendingRequests[senderId] = message.targetPlayerId;
            TradeRequestReceived?.Invoke(senderId, message.targetPlayerId);
            CheckForMatch();
        }
        else
        {
            PendingRequests.Remove(senderId);
        }

        TradeStateChanged?.Invoke();
    }

    private void HandleTradeOffer(TradeOfferMessage message, ulong senderId)
    {
        if (senderId == _localPlayerId) return;

        // Route to active session (local player is trading)
        if (ActiveSession != null && senderId == ActiveSession.PartnerPlayerId)
        {
            ActiveSession.PartnerOffer.Clear();
            if (message.cardDeckIndices != null)
                ActiveSession.PartnerOffer.CardDeckIndices.AddRange(message.cardDeckIndices);
            if (message.potionSlotIndices != null)
                ActiveSession.PartnerOffer.PotionSlotIndices.AddRange(message.potionSlotIndices);
            if (message.relicIndices != null)
                ActiveSession.PartnerOffer.RelicIndices.AddRange(message.relicIndices);

            if (ActiveSession.LocalConfirmed)
            {
                if (ActiveSession.PartnerOfferWhenLocalConfirmed != null &&
                    !ActiveSession.PartnerOffer.Equals(ActiveSession.PartnerOfferWhenLocalConfirmed))
                {
                    ActiveSession.LocalConfirmed = false;
                    ActiveSession.PartnerOfferWhenLocalConfirmed = null;
                    _netService.SendMessage(new TradeConfirmMessage { confirmed = false });
                }
            }

            if (ActiveSession.PartnerConfirmed)
                ActiveSession.PartnerConfirmed = false;

            OfferUpdated?.Invoke();
            TradeStateChanged?.Invoke();
            return;
        }

        // Route to observed session (local player is NOT trading, but needs state sync)
        if (_observedSession != null)
        {
            var targetOffer = GetObservedOffer(senderId);
            if (targetOffer != null)
            {
                targetOffer.Clear();
                if (message.cardDeckIndices != null)
                    targetOffer.CardDeckIndices.AddRange(message.cardDeckIndices);
                if (message.potionSlotIndices != null)
                    targetOffer.PotionSlotIndices.AddRange(message.potionSlotIndices);
                if (message.relicIndices != null)
                    targetOffer.RelicIndices.AddRange(message.relicIndices);
            }
        }
    }

    private void HandleTradeConfirm(TradeConfirmMessage message, ulong senderId)
    {
        if (senderId == _localPlayerId) return;

        // Route to active session
        if (ActiveSession != null && senderId == ActiveSession.PartnerPlayerId)
        {
            ActiveSession.PartnerConfirmed = message.confirmed;
            ConfirmChanged?.Invoke();

            if (ActiveSession.BothConfirmed)
                ExecuteTrade();

            TradeStateChanged?.Invoke();
            return;
        }

        // Route to observed session
        if (_observedSession != null)
        {
            if (senderId == _observedSession.LocalPlayerId)
                _observedSession.LocalConfirmed = message.confirmed;
            else if (senderId == _observedSession.PartnerPlayerId)
                _observedSession.PartnerConfirmed = message.confirmed;
            else
                return;

            if (_observedSession.BothConfirmed)
                ExecuteObservedTrade();
        }
    }

    private void HandleTradeCancel(TradeCancelMessage message, ulong senderId)
    {
        if (senderId == _localPlayerId) return;

        // Route to active session
        if (ActiveSession != null && senderId == ActiveSession.PartnerPlayerId)
        {
            CleanupTrade();
            TradeCancelled?.Invoke();
            TradeStateChanged?.Invoke();
            return;
        }

        // Route to observed session
        if (_observedSession != null &&
            (senderId == _observedSession.LocalPlayerId || senderId == _observedSession.PartnerPlayerId))
        {
            MainFile.Logger.Info($"Observed trade cancelled by {senderId}");
            _observedSession = null;
        }
    }

    /// <summary>
    /// Broadcasts the host's trade config to all clients.
    /// Called when TradeSynchronizer is created and when config changes mid-session.
    /// </summary>
    public void BroadcastConfig()
    {
        if (_netService is not INetHostGameService) return;

        MainFile.Logger.Info($"BroadcastConfig: UnlimitedTrades={TradeConfig.UnlimitedTrades}, BlockObtainHookRelics={TradeConfig.BlockObtainHookRelics}, BlockQuestCards={TradeConfig.BlockQuestCards}, Cards={TradeConfig.MaxCardSlots}, Potions={TradeConfig.MaxPotionSlots}, Relics={TradeConfig.MaxRelicSlots}");
        _netService.SendMessage(new TradeConfigMessage
        {
            UnlimitedTrades = TradeConfig.UnlimitedTrades,
            BlockObtainHookRelics = TradeConfig.BlockObtainHookRelics,
            BlockQuestCards = TradeConfig.BlockQuestCards,
            MaxCardSlots = TradeConfig.MaxCardSlots,
            MaxPotionSlots = TradeConfig.MaxPotionSlots,
            MaxRelicSlots = TradeConfig.MaxRelicSlots,
        });
    }

    private void HandleTradeConfig(TradeConfigMessage message, ulong senderId)
    {
        // Only clients apply the host's config; the host ignores its own broadcast
        if (_netService is INetHostGameService) return;

        MainFile.Logger.Info($"HandleTradeConfig: Applying host config - UnlimitedTrades={message.UnlimitedTrades}, BlockObtainHookRelics={message.BlockObtainHookRelics}, BlockQuestCards={message.BlockQuestCards}, Cards={message.MaxCardSlots}, Potions={message.MaxPotionSlots}, Relics={message.MaxRelicSlots}");
        TradeConfig.UnlimitedTrades = message.UnlimitedTrades;
        TradeConfig.BlockObtainHookRelics = message.BlockObtainHookRelics;
        TradeConfig.BlockQuestCards = message.BlockQuestCards;
        TradeConfig.MaxCardSlots = message.MaxCardSlots;
        TradeConfig.MaxPotionSlots = message.MaxPotionSlots;
        TradeConfig.MaxRelicSlots = message.MaxRelicSlots;
    }

    /// <summary>
    /// Gets the offer slot in the observed session that corresponds to the given sender.
    /// </summary>
    private TradeOffer? GetObservedOffer(ulong senderId)
    {
        if (_observedSession == null) return null;
        if (senderId == _observedSession.LocalPlayerId) return _observedSession.LocalOffer;
        if (senderId == _observedSession.PartnerPlayerId) return _observedSession.PartnerOffer;
        return null;
    }

    // =========================================================================
    // Match Detection
    // =========================================================================

    private void CheckForMatch()
    {
        // Check for local player match first
        if (Phase != TradePhase.Trading && Phase != TradePhase.Completed)
        {
            if (PendingRequests.TryGetValue(_localPlayerId, out ulong localTarget))
            {
                if (PendingRequests.TryGetValue(localTarget, out ulong remoteTarget))
                {
                    MainFile.Logger.Info($"CheckForMatch: localTarget={localTarget}, remoteTarget={remoteTarget}, localId={_localPlayerId}");
                    if (remoteTarget == _localPlayerId)
                    {
                        BeginTradeSession(localTarget);
                        return;
                    }
                }
                else
                {
                    MainFile.Logger.Info($"CheckForMatch: no remote target yet (localTarget={localTarget})");
                }
            }
            else
            {
                MainFile.Logger.Info($"CheckForMatch: no local target yet (localId={_localPlayerId})");
            }
        }

        // Check for matches between OTHER players (observed trades for 3+ player sync).
        // This follows the same pattern as EventSynchronizer: each machine independently
        // detects the match and executes the same logic using Cmd methods.
        if (_observedSession != null) return; // already observing a trade

        foreach (var kvp in PendingRequests)
        {
            var playerA = kvp.Key;
            var targetA = kvp.Value;
            // Skip if local player is involved — that's handled by the active session above
            if (playerA == _localPlayerId || targetA == _localPlayerId) continue;
            if (PendingRequests.TryGetValue(targetA, out var targetB) && targetB == playerA)
            {
                BeginObservedSession(playerA, targetA);
                break;
            }
        }
    }

    private void BeginTradeSession(ulong partnerId)
    {
        Phase = TradePhase.Trading;
        ActiveSession = new TradeSession
        {
            LocalPlayerId = _localPlayerId,
            PartnerPlayerId = partnerId
        };

        PendingRequests.Remove(_localPlayerId);
        PendingRequests.Remove(partnerId);

        MainFile.Logger.Info($"Trade session started between {_localPlayerId} and {partnerId}");
        TradeStarted?.Invoke();
        TradeStateChanged?.Invoke();
    }

    private void BeginObservedSession(ulong playerA, ulong playerB)
    {
        // Canonical ordering: lower NetId = "Local" side, higher = "Partner" side
        var (first, second) = playerA < playerB ? (playerA, playerB) : (playerB, playerA);
        _observedSession = new TradeSession
        {
            LocalPlayerId = first,
            PartnerPlayerId = second
        };

        PendingRequests.Remove(playerA);
        PendingRequests.Remove(playerB);

        MainFile.Logger.Info($"Observing trade between {first} and {second} (local player {_localPlayerId} is not involved)");
    }

    // =========================================================================
    // Trade Execution — uses the game's Cmd API (same pattern as EventSynchronizer)
    // =========================================================================

    private async void ExecuteTrade()
    {
        if (ActiveSession == null) return;

        var localPlayer = _playerCollection.GetPlayer(ActiveSession.LocalPlayerId);
        var partnerPlayer = _playerCollection.GetPlayer(ActiveSession.PartnerPlayerId);

        if (localPlayer == null || partnerPlayer == null)
        {
            MainFile.Logger.Error("Trade execution failed: player not found");
            return;
        }

        MainFile.Logger.Info($"Executing trade between {localPlayer.NetId} and {partnerPlayer.NetId}");

        PlayersWhoTraded.Add(ActiveSession.LocalPlayerId);
        PlayersWhoTraded.Add(ActiveSession.PartnerPlayerId);

        // Only mark JustCompletedTrade when OnSelect will return true (non-unlimited).
        // JustCompletedTrade tells the ShouldDisableRemainingRestSiteOptions hook to
        // return false so the trade doesn't consume the rest action. In unlimited mode,
        // OnSelect returns false (option stays), so the hook never fires and the entry
        // lingers — then when the player picks Rest, the hook incorrectly suppresses it,
        // making Rest not consume the action either.
        if (!TradeConfig.UnlimitedTrades)
        {
            JustCompletedTrade.Add(ActiveSession.LocalPlayerId);
            JustCompletedTrade.Add(ActiveSession.PartnerPlayerId);
        }

        Phase = TradePhase.Completed;

        var pendingRelicObtains = new List<(SerializableRelic serializable, Player target, int preExpand)>();
        try
        {
            pendingRelicObtains = await ExecuteTradeCoreAsync(
                localPlayer, ActiveSession.LocalOffer,
                partnerPlayer, ActiveSession.PartnerOffer);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"ExecuteTrade: ExecuteTradeCoreAsync threw: {e}");
        }

        MainFile.Logger.Info("Trade executed successfully");
        var completedA = ActiveSession.LocalPlayerId;
        var completedB = ActiveSession.PartnerPlayerId;
        TradeCompleted?.Invoke(completedA, completedB);
        TradeStateChanged?.Invoke();

        // Clear the active session AFTER TradeCompleted so RunTradeUI resolves,
        // but BEFORE any new trade can start. Without this, stale ActiveSession
        // routes messages from the ex-partner to the dead session instead of a
        // new _observedSession — causing 3-player desync when the ex-partner
        // trades with someone else next.
        ActiveSession = null;
        Phase = TradePhase.Idle;

        // Run RelicCmd.Obtain AFTER TradeCompleted closes the trade screen.
        // This allows interactive AfterObtained hooks (card selection, etc.)
        // to show their UI. Canonical ordering ensures all machines process
        // relics in the same order, preventing desync on PlayerChoices.
        await ObtainPendingRelics(pendingRelicObtains);
    }

    private async void ExecuteObservedTrade()
    {
        if (_observedSession == null) return;

        var playerA = _playerCollection.GetPlayer(_observedSession.LocalPlayerId);
        var playerB = _playerCollection.GetPlayer(_observedSession.PartnerPlayerId);

        if (playerA == null || playerB == null)
        {
            MainFile.Logger.Error("Observed trade execution failed: player not found");
            _observedSession = null;
            return;
        }

        MainFile.Logger.Info($"Executing observed trade between {playerA.NetId} and {playerB.NetId} (local={_localPlayerId})");

        PlayersWhoTraded.Add(_observedSession.LocalPlayerId);
        PlayersWhoTraded.Add(_observedSession.PartnerPlayerId);
        if (!TradeConfig.UnlimitedTrades)
        {
            JustCompletedTrade.Add(_observedSession.LocalPlayerId);
            JustCompletedTrade.Add(_observedSession.PartnerPlayerId);
        }

        var pendingRelicObtains = new List<(SerializableRelic serializable, Player target, int preExpand)>();
        try
        {
            pendingRelicObtains = await ExecuteTradeCoreAsync(
                playerA, _observedSession.LocalOffer,
                playerB, _observedSession.PartnerOffer);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"ExecuteObservedTrade: ExecuteTradeCoreAsync threw: {e}");
        }

        MainFile.Logger.Info("Observed trade executed successfully");
        var observedA = _observedSession.LocalPlayerId;
        var observedB = _observedSession.PartnerPlayerId;
        _observedSession = null;

        // Fire TradeCompleted so the observer machine's OnSelectRemote
        // resolves as True for both trading players. Without this, the
        // OnSelectRemote watchers would only resolve via timeout or the
        // Phase-based OnStateChanged check (which can fire prematurely
        // if the local player cancels their own unrelated trade).
        TradeCompleted?.Invoke(observedA, observedB);
        TradeStateChanged?.Invoke();

        // Run RelicCmd.Obtain AFTER TradeCompleted (same as ExecuteTrade).
        await ObtainPendingRelics(pendingRelicObtains);
    }

    /// <summary>
    /// Obtains traded relics via RelicCmd.Obtain after the trade screen has
    /// closed. Each player's relics are obtained concurrently so both players
    /// can interact with AfterObtained hooks (card selection, etc.) at the
    /// same time rather than one player waiting for the other.
    ///
    /// Within each player's relics, they're processed sequentially. Across
    /// players, Task.WhenAll runs them in parallel. This is safe because
    /// AfterObtained hooks only trigger PlayerChoices for their own player,
    /// so different players' choices are independent.
    /// </summary>
    private async Task ObtainPendingRelics(List<(SerializableRelic serializable, Player target, int preExpand)> pendingRelicObtains)
    {
        if (pendingRelicObtains.Count == 0) return;

        MainFile.Logger.Info($"=== OBTAINING {pendingRelicObtains.Count} TRADED RELICS ===");

        // Group by target player, then obtain each player's relics concurrently
        var byPlayer = pendingRelicObtains
            .GroupBy(r => r.target.NetId)
            .OrderBy(g => g.Key) // canonical order for logging consistency
            .ToList();

        var tasks = byPlayer.Select(group => ObtainRelicsForPlayer(group.ToList()));
        await Task.WhenAll(tasks);

        MainFile.Logger.Info($"=== ALL TRADED RELICS OBTAINED ===");
    }

    private async Task ObtainRelicsForPlayer(List<(SerializableRelic serializable, Player target, int preExpand)> relics)
    {
        foreach (var (serializable, target, preExpand) in relics)
        {
            try
            {
                var fresh = RelicModel.FromSerializable(serializable);
                MainFile.Logger.Info($"  RelicCmd.Obtain: {fresh.Id.Entry} -> {target.NetId}, relics before: {target.Relics.Count}");
                await RelicCmd.Obtain(fresh, target);
                MainFile.Logger.Info($"  After Obtain: {target.NetId} relics={target.Relics.Count}");

                // PotionBelt: undo pre-expansion since Obtain fires AfterObtained
                // which expands slots again
                if (fresh is PotionBelt obtainedBelt && preExpand > 0)
                {
                    int slots = obtainedBelt.DynamicVars["PotionSlots"].IntValue;
                    MainFile.Logger.Info($"    Undoing pre-expansion of {slots} for PotionBelt on {target.NetId}");
                    await PlayerCmd.LoseMaxPotionCount(slots, target);
                }
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"  RelicCmd.Obtain FAILED for {serializable.Id?.Entry} to {target.NetId}: {e}");
            }
        }
    }

    /// <summary>
    /// Logs the full state of a player's deck, relics, and potions.
    /// Deck.Cards.Count is what drives the card count in the top-right UI
    /// (NTopBarDeckButton subscribes to CardPile add/remove events).
    /// </summary>
    private void LogPlayerState(string phase, Player player)
    {
        try
        {
            var deckCount = player.Deck?.Cards?.Count ?? -1;
            var deckCards = player.Deck?.Cards != null
                ? string.Join(", ", player.Deck.Cards.Select(c => $"{c.Id.Entry}(owner={c.Owner?.NetId})"))
                : "null";
            var relicCount = player.Relics?.Count ?? -1;
            var relicList = player.Relics != null
                ? string.Join(", ", player.Relics.Select(r => r.Id.Entry))
                : "null";
            var potionInfo = player.PotionSlots != null
                ? string.Join(", ", player.PotionSlots.Select((p, i) => $"[{i}]={p?.Id.Entry ?? "empty"}"))
                : "null";
            MainFile.Logger.Info($"[{phase}] Player {player.NetId}: " +
                                $"DeckCount={deckCount} ({deckCards}), " +
                                $"RelicCount={relicCount} ({relicList}), " +
                                $"Potions=({potionInfo})");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{phase}] Failed to log player {player.NetId} state: {e.Message}");
        }
    }

    /// <summary>
    /// Core trade execution using the game's Cmd API — the same pattern used by
    /// EventSynchronizer. Each machine independently runs the same Cmd methods,
    /// which handle hooks, VFX (suppressed for non-local players via LocalContext),
    /// history, and state management.
    ///
    /// Phase 1: Remove ALL items from both players (frees potion slots first).
    /// Phase 2: Add ALL items to destination players.
    /// </summary>
    /// <summary>
    /// Core trade logic: removes items from both players and adds them to the other.
    /// Relics are added via AddRelicInternal (no AfterObtained hooks) and returned
    /// as pending hooks for the caller to fire after the trade UI closes.
    /// </summary>
    private async Task<List<(SerializableRelic serializable, Player target, int preExpand)>> ExecuteTradeCoreAsync(
        Player playerA, TradeOffer offerA,
        Player playerB, TradeOffer offerB)
    {
        var runStateA = playerA.RunState;
        var runStateB = playerB.RunState;

        try
        {
            MainFile.Logger.Info($"=== TRADE EXECUTION START ===");
            MainFile.Logger.Info($"PlayerA={playerA.NetId}, PlayerB={playerB.NetId}");
            MainFile.Logger.Info($"RunState same object: {ReferenceEquals(runStateA, runStateB)}");

            // Log full state BEFORE trade
            LogPlayerState("BEFORE TRADE", playerA);
            LogPlayerState("BEFORE TRADE", playerB);

            // === Phase 0: Snapshot and clone items BEFORE any modifications ===

            // Cards: clone before removal so HasBeenRemovedFromState stays false on the clone
            var aCards = offerA.CardDeckIndices
                .Where(i => i >= 0 && i < playerA.Deck.Cards.Count)
                .Select(i => playerA.Deck.Cards[i])
                .ToList();
            var bCards = offerB.CardDeckIndices
                .Where(i => i >= 0 && i < playerB.Deck.Cards.Count)
                .Select(i => playerB.Deck.Cards[i])
                .ToList();

            MainFile.Logger.Info($"Phase 0: A offers cards at indices [{string.Join(",", offerA.CardDeckIndices)}] = [{string.Join(",", aCards.Select(c => c.Id.Entry))}]");
            MainFile.Logger.Info($"Phase 0: B offers cards at indices [{string.Join(",", offerB.CardDeckIndices)}] = [{string.Join(",", bCards.Select(c => c.Id.Entry))}]");

            var aCardClones = aCards.Select(c => (CardModel)c.ClonePreservingMutability()).ToList();
            var bCardClones = bCards.Select(c => (CardModel)c.ClonePreservingMutability()).ToList();

            foreach (var clone in aCardClones)
                MainFile.Logger.Info($"  Clone A: {clone.Id.Entry}, Owner={clone.Owner?.NetId}, HasBeenRemovedFromState={clone.HasBeenRemovedFromState}, Pile={clone.Pile?.Type}");
            foreach (var clone in bCardClones)
                MainFile.Logger.Info($"  Clone B: {clone.Id.Entry}, Owner={clone.Owner?.NetId}, HasBeenRemovedFromState={clone.HasBeenRemovedFromState}, Pile={clone.Pile?.Type}");

            // Potions: save IDs for creating fresh instances after removal
            var aPotions = offerA.PotionSlotIndices
                .Where(i => i >= 0 && i < playerA.PotionSlots.Count && playerA.PotionSlots[i] != null)
                .Select(i => playerA.PotionSlots[i]!)
                .ToList();
            var bPotions = offerB.PotionSlotIndices
                .Where(i => i >= 0 && i < playerB.PotionSlots.Count && playerB.PotionSlots[i] != null)
                .Select(i => playerB.PotionSlots[i]!)
                .ToList();

            var aPotionIds = aPotions.Select(p => p.Id).ToList();
            var bPotionIds = bPotions.Select(p => p.Id).ToList();

            MainFile.Logger.Info($"Phase 0: A offers potions [{string.Join(",", aPotionIds.Select(id => id.Entry))}]");
            MainFile.Logger.Info($"Phase 0: B offers potions [{string.Join(",", bPotionIds.Select(id => id.Entry))}]");

            // Relics: serialize state before removal (preserves SavedProperty counters/flags)
            var aRelics = offerA.RelicIndices
                .Where(i => i >= 0 && i < playerA.Relics.Count)
                .Select(i => playerA.Relics[i])
                .ToList();
            var bRelics = offerB.RelicIndices
                .Where(i => i >= 0 && i < playerB.Relics.Count)
                .Select(i => playerB.Relics[i])
                .ToList();

            var aRelicSerializables = aRelics.Select(r => r.ToSerializable()).ToList();
            var bRelicSerializables = bRelics.Select(r => r.ToSerializable()).ToList();

            MainFile.Logger.Info($"Phase 0: A offers relics [{string.Join(",", aRelics.Select(r => r.Id.Entry))}]");
            MainFile.Logger.Info($"Phase 0: B offers relics [{string.Join(",", bRelics.Select(r => r.Id.Entry))}]");

            // === Phase 1: Remove ALL items from both players ===
            // Removals first so potion slots are freed before additions.

            MainFile.Logger.Info($"=== Phase 1: REMOVALS ===");

            foreach (var card in aCards)
            {
                MainFile.Logger.Info($"  Removing card {card.Id.Entry} from A ({playerA.NetId}), InDeck={card.Pile?.Type}");
                await CardPileCmd.RemoveFromDeck(card, showPreview: false);
                MainFile.Logger.Info($"    After removal: HasBeenRemovedFromState={card.HasBeenRemovedFromState}");
            }
            foreach (var card in bCards)
            {
                MainFile.Logger.Info($"  Removing card {card.Id.Entry} from B ({playerB.NetId}), InDeck={card.Pile?.Type}");
                await CardPileCmd.RemoveFromDeck(card, showPreview: false);
                MainFile.Logger.Info($"    After removal: HasBeenRemovedFromState={card.HasBeenRemovedFromState}");
            }

            foreach (var potion in aPotions)
            {
                MainFile.Logger.Info($"  Discarding potion {potion.Id.Entry} from A ({playerA.NetId})");
                await PotionCmd.Discard(potion);
            }
            foreach (var potion in bPotions)
            {
                MainFile.Logger.Info($"  Discarding potion {potion.Id.Entry} from B ({playerB.NetId})");
                await PotionCmd.Discard(potion);
            }

            foreach (var relic in aRelics)
            {
                MainFile.Logger.Info($"  Removing relic {relic.Id.Entry} from A ({playerA.NetId})");
                await RelicCmd.Remove(relic);
                // PotionBelt.AfterRemoved doesn't reduce MaxPotionCount, so do it manually
                if (relic is PotionBelt beltA)
                {
                    int slots = beltA.DynamicVars["PotionSlots"].IntValue;
                    MainFile.Logger.Info($"  PotionBelt removed from A — reducing MaxPotionCount by {slots}");
                    await PlayerCmd.LoseMaxPotionCount(slots, playerA);
                }
            }
            foreach (var relic in bRelics)
            {
                MainFile.Logger.Info($"  Removing relic {relic.Id.Entry} from B ({playerB.NetId})");
                await RelicCmd.Remove(relic);
                if (relic is PotionBelt beltB)
                {
                    int slots = beltB.DynamicVars["PotionSlots"].IntValue;
                    MainFile.Logger.Info($"  PotionBelt removed from B — reducing MaxPotionCount by {slots}");
                    await PlayerCmd.LoseMaxPotionCount(slots, playerB);
                }
            }

            // Log state after all removals
            LogPlayerState("AFTER REMOVALS", playerA);
            LogPlayerState("AFTER REMOVALS", playerB);

            // === Phase 2: Add ALL items to destination players ===
            MainFile.Logger.Info($"=== Phase 2: ADDITIONS ===");

            // A's cards go to B
            foreach (var clone in aCardClones)
            {
                MainFile.Logger.Info($"  Adding card {clone.Id.Entry} to B ({playerB.NetId})");
                MainFile.Logger.Info($"    Before: Owner={clone.Owner?.NetId}, HasBeenRemovedFromState={clone.HasBeenRemovedFromState}, Pile={clone.Pile?.Type}");
                clone.Owner = null;
                runStateA.AddCard(clone, playerB);
                var inRunState = runStateA.ContainsCard(clone);
                MainFile.Logger.Info($"    After AddCard: Owner={clone.Owner?.NetId}, InRunState={inRunState}, Pile={clone.Pile?.Type}");
                var addResult = await CardPileCmd.Add(clone, PileType.Deck, skipVisuals: false);
                MainFile.Logger.Info($"    CardPileCmd.Add result: success={addResult.success}, cardAdded={addResult.cardAdded?.Id.Entry}, oldPile={addResult.oldPile?.Type}");
                MainFile.Logger.Info($"    After Add: Pile={clone.Pile?.Type}, B DeckCount={playerB.Deck.Cards.Count}");
            }
            // NTopBarDeckButton listens to CardAddFinished (not CardAdded).
            // CardPileCmd.Add only fires CardAdded via AddInternal — CardAddFinished
            // is normally fired by NCardFlyVfx after animation. Since we have no
            // fly animation, invoke it manually so the deck counter updates.
            playerB.Deck.InvokeCardAddFinished();

            // B's cards go to A
            foreach (var clone in bCardClones)
            {
                MainFile.Logger.Info($"  Adding card {clone.Id.Entry} to A ({playerA.NetId})");
                MainFile.Logger.Info($"    Before: Owner={clone.Owner?.NetId}, HasBeenRemovedFromState={clone.HasBeenRemovedFromState}, Pile={clone.Pile?.Type}");
                clone.Owner = null;
                runStateA.AddCard(clone, playerA);
                var inRunState = runStateA.ContainsCard(clone);
                MainFile.Logger.Info($"    After AddCard: Owner={clone.Owner?.NetId}, InRunState={inRunState}, Pile={clone.Pile?.Type}");
                var addResult = await CardPileCmd.Add(clone, PileType.Deck, skipVisuals: false);
                MainFile.Logger.Info($"    CardPileCmd.Add result: success={addResult.success}, cardAdded={addResult.cardAdded?.Id.Entry}, oldPile={addResult.oldPile?.Type}");
                MainFile.Logger.Info($"    After Add: Pile={clone.Pile?.Type}, A DeckCount={playerA.Deck.Cards.Count}");
            }
            playerA.Deck.InvokeCardAddFinished();

            // Potions BEFORE relics to prevent desync.
            //
            // Relics like LOST_COFFER trigger AfterObtained hooks that grant
            // rewards (potions, cards). On the participant machine these rewards
            // are processed synchronously during the await (player picks from
            // reward screen), but on observer machines the rewards arrive later
            // via RewardObtainedMessage. If traded potions are added AFTER
            // relics, they end up in different potion slots on different
            // machines — causing a checksum desync.
            //
            // To handle PotionBelt (which grants extra potion slots in its
            // AfterObtained hook): pre-expand slots before adding potions, then
            // compensate after RelicCmd.Obtain to avoid double-expansion.

            // Canonical ordering for relics: always process lower-NetId player's
            // relics first. This ensures deterministic ordering of the pending
            // relic list returned to the caller.
            bool canonSwap = playerA.NetId > playerB.NetId;
            var firstRelicSerializables = canonSwap ? bRelicSerializables : aRelicSerializables;
            var firstRelicTarget = canonSwap ? playerA : playerB;
            var secondRelicSerializables = canonSwap ? aRelicSerializables : bRelicSerializables;
            var secondRelicTarget = canonSwap ? playerB : playerA;

            MainFile.Logger.Info($"  Canonical relic order: first={firstRelicTarget.NetId}'s incoming relics, second={secondRelicTarget.NetId}'s incoming relics (swap={canonSwap})");

            // Pre-expand potion slots for any incoming PotionBelt
            int preExpandFirst = 0, preExpandSecond = 0;
            foreach (var serializable in firstRelicSerializables)
            {
                if (serializable.Id?.Entry == "POTION_BELT")
                {
                    var temp = RelicModel.FromSerializable(serializable) as PotionBelt;
                    if (temp != null)
                    {
                        int slots = temp.DynamicVars["PotionSlots"].IntValue;
                        MainFile.Logger.Info($"  Pre-expanding {firstRelicTarget.NetId} max potion count by {slots} for incoming PotionBelt");
                        await PlayerCmd.GainMaxPotionCount(slots, firstRelicTarget);
                        preExpandFirst += slots;
                    }
                }
            }
            foreach (var serializable in secondRelicSerializables)
            {
                if (serializable.Id?.Entry == "POTION_BELT")
                {
                    var temp = RelicModel.FromSerializable(serializable) as PotionBelt;
                    if (temp != null)
                    {
                        int slots = temp.DynamicVars["PotionSlots"].IntValue;
                        MainFile.Logger.Info($"  Pre-expanding {secondRelicTarget.NetId} max potion count by {slots} for incoming PotionBelt");
                        await PlayerCmd.GainMaxPotionCount(slots, secondRelicTarget);
                        preExpandSecond += slots;
                    }
                }
            }

            // A's potions go to B
            foreach (var potionId in aPotionIds)
            {
                MainFile.Logger.Info($"  Adding potion {potionId.Entry} to B ({playerB.NetId})");
                var fresh = ModelDb.GetById<PotionModel>(potionId).ToMutable();
                var result = await PotionCmd.TryToProcure(fresh, playerB);
                MainFile.Logger.Info($"    PotionCmd.TryToProcure result: success={result.success}, failureReason={result.failureReason}");
            }
            // B's potions go to A
            foreach (var potionId in bPotionIds)
            {
                MainFile.Logger.Info($"  Adding potion {potionId.Entry} to A ({playerA.NetId})");
                var fresh = ModelDb.GetById<PotionModel>(potionId).ToMutable();
                var result = await PotionCmd.TryToProcure(fresh, playerA);
                MainFile.Logger.Info($"    PotionCmd.TryToProcure result: success={result.success}, failureReason={result.failureReason}");
            }

            // Relics are deferred — RelicCmd.Obtain fires AfterObtained hooks
            // that can open interactive UI (card selection, etc.). We collect
            // the pending relic additions here and return them so the caller
            // can run RelicCmd.Obtain AFTER the trade screen closes.
            var pendingRelicObtains = new List<(SerializableRelic serializable, Player target, int preExpand)>();

            foreach (var serializable in firstRelicSerializables)
                pendingRelicObtains.Add((serializable, firstRelicTarget, preExpandFirst));
            foreach (var serializable in secondRelicSerializables)
                pendingRelicObtains.Add((serializable, secondRelicTarget, preExpandSecond));

            // Log full state AFTER trade
            LogPlayerState("AFTER TRADE", playerA);
            LogPlayerState("AFTER TRADE", playerB);

            MainFile.Logger.Info($"=== TRADE EXECUTION COMPLETE (pending {pendingRelicObtains.Count} relic obtains) ===");
            return pendingRelicObtains;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Trade execution FAILED with exception: {e}");
            // Log state even on failure to see partial progress
            try
            {
                LogPlayerState("AFTER FAILURE", playerA);
                LogPlayerState("AFTER FAILURE", playerB);
            }
            catch { /* ignore logging failures */ }
            return new List<(SerializableRelic, Player, int)>();
        }
    }

    private void CleanupTrade()
    {
        ActiveSession = null;
        Phase = TradePhase.Idle;
        PendingRequests.Remove(_localPlayerId);
    }

    public Player? GetLocalPlayer() => _playerCollection.GetPlayer(_localPlayerId);
    public Player? GetPartnerPlayer() => ActiveSession != null ? _playerCollection.GetPlayer(ActiveSession.PartnerPlayerId) : null;

    public IEnumerable<Player> GetOtherPlayers()
    {
        return _playerCollection.Players.Where(p => p.NetId != _localPlayerId);
    }
}
