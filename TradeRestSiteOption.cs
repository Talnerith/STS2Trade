using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using STS2Trade.UI;

namespace STS2Trade;

public sealed class TradeRestSiteOption : RestSiteOption
{
    private TaskCompletionSource<bool>? _tradeCompletionSource;
    private CancellationTokenSource? _waitCts;
    private NThoughtBubbleVfx? _waitingBubble;

    public override string OptionId => "TRADE";

    public override LocString Description
    {
        get
        {
            if (!IsEnabled)
            {
                return new LocString("rest_site_ui", "OPTION_TRADE.descriptionDisabled");
            }
            return new LocString("rest_site_ui", "OPTION_TRADE.description");
        }
    }

    public TradeRestSiteOption(Player owner) : base(owner)
    {
        var sync = TradeSynchronizer.Instance;
        IsEnabled = owner.RunState.Players.Count > 1
            && (sync == null || sync.CanTrade(owner.NetId));
        MainFile.Logger.Info($"[TradeRestSiteOption] IsEnabled={IsEnabled} for {owner.NetId}, UnlimitedTrades={TradeConfig.UnlimitedTrades}");
    }

    public override async Task<bool> OnSelect()
    {
        // Reserve a choice ID following the MendRestSiteOption pattern.
        // This ensures HOST and CLIENT track the same number of choices per player,
        // preventing Choice ID desync in the checksum.
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(Owner);
        MainFile.Logger.Info($"OnSelect: Reserved choiceId={choiceId} for player {Owner.NetId}");

        var sync = TradeSynchronizer.Instance;
        if (sync == null)
        {
            MainFile.Logger.Error("TradeSynchronizer not available");
            // Still sync the choice so counters stay consistent
            if (LocalContext.IsMe(Owner))
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(Owner, choiceId, PlayerChoiceResult.FromPlayerId(null));
            else
                await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(Owner, choiceId);
            return false;
        }

        if (LocalContext.IsMe(Owner))
        {
            return await OnSelectLocal(sync, choiceId);
        }
        else
        {
            return await OnSelectRemote(sync, choiceId);
        }
    }

    /// <summary>
    /// Local player selected Trade: show targeting UI, wait for partner, open trade screen.
    /// </summary>
    private async Task<bool> OnSelectLocal(TradeSynchronizer sync, uint choiceId)
    {
        MainFile.Logger.Info("OnSelectLocal: Starting trade selection flow");
        sync.Phase = TradePhase.SelectingPartner;
        sync.NotifyStateChanged();

        Player? target = await SelectTradePartner();

        if (target == null)
        {
            // Sync the choice with null so counters stay consistent
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                Owner, choiceId, PlayerChoiceResult.FromPlayerId(null));
            MainFile.Logger.Info("OnSelectLocal: No target selected, cancelling");
            sync.Phase = TradePhase.Idle;
            sync.NotifyStateChanged();
            return false;
        }

        // Validate that the target can actually trade
        if (!CanTargetTrade(target, sync))
        {
            MainFile.Logger.Info($"OnSelectLocal: Target {target.NetId} cannot trade, showing bubble");
            ShowCantTradeBubble();
            // Sync with null — this is effectively a cancellation
            RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                Owner, choiceId, PlayerChoiceResult.FromPlayerId(null));
            sync.Phase = TradePhase.Idle;
            sync.NotifyStateChanged();
            return false;
        }

        // Sync the choice regardless of outcome (MendRestSiteOption pattern).
        // This ensures the remote side's WaitForRemoteChoice completes and
        // choice counters stay in sync on both machines.
        RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
            Owner, choiceId, PlayerChoiceResult.FromPlayerId(target.NetId));
        MainFile.Logger.Info($"OnSelectLocal: Synced choice with target={target.NetId}");
        sync.SelectTarget(target.NetId);
        MainFile.Logger.Info($"OnSelectLocal: After SelectTarget, Phase={sync.Phase}");

        if (sync.Phase != TradePhase.Trading)
        {
            // Show a "waiting for [player]" thought bubble on our character
            ShowWaitingBubble(target);

            MainFile.Logger.Info("OnSelectLocal: Waiting for partner match...");
            bool matched = await WaitForPartnerMatch(target.NetId);

            // Remove the waiting bubble
            RemoveWaitingBubble();

            if (!matched)
            {
                MainFile.Logger.Info("OnSelectLocal: Partner match failed/cancelled");
                sync.DeselectTarget();
                return false;
            }
            MainFile.Logger.Info("OnSelectLocal: Partner matched!");
        }
        else
        {
            MainFile.Logger.Info("OnSelectLocal: Already in Trading phase, skipping wait");
        }

        MainFile.Logger.Info("OnSelectLocal: Opening trade UI...");
        try
        {
            bool tradeResult = await RunTradeUI();
            MainFile.Logger.Info($"OnSelectLocal: Trade UI result: {tradeResult}");

            if (tradeResult && TradeConfig.UnlimitedTrades)
            {
                // In unlimited mode, return false so the game doesn't remove
                // this option from the rest site list — player can trade again.
                MainFile.Logger.Info("OnSelectLocal: Unlimited trades — keeping option available");
                return false;
            }

            return tradeResult;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"OnSelectLocal: RunTradeUI threw exception: {e}");
            return false;
        }
    }

    /// <summary>
    /// Remote player selected Trade: wait for the synced choice, then wait for trade resolution.
    /// The actual trade UI and messaging is handled by TradeSynchronizer on the local machine.
    /// </summary>
    private async Task<bool> OnSelectRemote(TradeSynchronizer sync, uint choiceId)
    {
        MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} waiting for remote choice (choiceId={choiceId})...");

        // Wait for the remote player's choice (who they targeted).
        // This keeps choice counters in sync with the local machine.
        var choiceResult = await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(Owner, choiceId);
        ulong? targetId = choiceResult.AsPlayerId();
        MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} remote choice received: targetId={targetId}");

        if (targetId == null)
        {
            // Remote player cancelled targeting — return false so options re-show
            MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} cancelled targeting");
            return false;
        }

        // Remote player selected a valid target. Now wait for the trade to complete or cancel.
        MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} targeted {targetId}, waiting for trade resolution...");

        var tcs = new TaskCompletionSource<bool>();
        var ownerNetId = Owner.NetId;

        void OnCompleted(ulong pA, ulong pB)
        {
            // Only resolve if THIS player was part of the completed trade.
            // Without this filter, a trade between two other players in a 4-player
            // game would resolve all pending OnSelectRemote watchers, removing the
            // trade option for uninvolved players.
            if (pA == ownerNetId || pB == ownerNetId)
                tcs.TrySetResult(true);
        }
        void OnCancelled() => tcs.TrySetResult(false);

        sync.TradeCompleted += OnCompleted;
        sync.TradeCancelled += OnCancelled;

        try
        {
            var timeoutTask = Task.Delay(180000); // 3 minute timeout
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} timed out waiting for trade resolution");
                return false;
            }

            bool result = await tcs.Task;
            MainFile.Logger.Info($"OnSelectRemote: Player {Owner.NetId} trade resolved: {result}");

            // In unlimited mode, return false so the option stays in the list
            if (result && TradeConfig.UnlimitedTrades)
            {
                MainFile.Logger.Info($"OnSelectRemote: Unlimited trades — keeping option available for {Owner.NetId}");
                return false;
            }

            return result;
        }
        finally
        {
            sync.TradeCompleted -= OnCompleted;
            sync.TradeCancelled -= OnCancelled;
        }
    }

    private async Task<Player?> SelectTradePartner()
    {
        var restSiteRoom = NRestSiteRoom.Instance;
        if (restSiteRoom == null) return null;

        var button = restSiteRoom.GetButtonForOption(this);
        if (button == null) return null;

        Vector2 startPosition = button.GlobalPosition + button.Size / 2f;
        var targetManager = NTargetManager.Instance;

        targetManager.StartTargeting(
            TargetType.AnyPlayer,
            startPosition,
            TargetMode.ClickMouseToTarget,
            ShouldCancelTargeting,
            AllowTargetingNode
        );

        try
        {
            var result = await targetManager.SelectionFinished();
            return NodeToPlayer(result);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> WaitForPartnerMatch(ulong targetId)
    {
        var sync = TradeSynchronizer.Instance;
        if (sync == null) return false;

        _waitCts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<bool>();

        void OnTradeStarted()
        {
            MainFile.Logger.Info("WaitForPartnerMatch: TradeStarted event received!");
            tcs.TrySetResult(true);
        }
        void OnCancelled()
        {
            MainFile.Logger.Info("WaitForPartnerMatch: TradeCancelled event received");
            tcs.TrySetResult(false);
        }

        sync.TradeStarted += OnTradeStarted;
        sync.TradeCancelled += OnCancelled;

        // Create an input handler node to listen for Escape/right-click during waiting
        WaitingInputHandler? inputHandler = null;
        try
        {
            inputHandler = new WaitingInputHandler();
            inputHandler.Setup(() =>
            {
                MainFile.Logger.Info("WaitForPartnerMatch: User cancelled via input");
                _waitCts?.Cancel();
                tcs.TrySetResult(false);
            });

            // Add to a valid parent node
            Node? inputParent = NRun.Instance?.GlobalUi ?? NRestSiteRoom.Instance as Node;
            if (inputParent != null)
            {
                inputParent.AddChildSafely(inputHandler);
                MainFile.Logger.Info("WaitForPartnerMatch: Input handler added to scene tree");
            }
            else
            {
                MainFile.Logger.Error("WaitForPartnerMatch: No valid parent for input handler!");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"WaitForPartnerMatch: Failed to create input handler: {e.Message}");
        }

        try
        {
            var timeoutTask = Task.Delay(120000); // 2 minute timeout
            var cancelTask = WaitForCancellation(_waitCts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask, cancelTask);

            if (completedTask == timeoutTask || completedTask == cancelTask)
            {
                MainFile.Logger.Info($"WaitForPartnerMatch: Ended by {(completedTask == timeoutTask ? "timeout" : "cancellation")}");
                return false;
            }

            bool result = await tcs.Task;
            MainFile.Logger.Info($"WaitForPartnerMatch: Resolved with result={result}");
            return result;
        }
        finally
        {
            sync.TradeStarted -= OnTradeStarted;
            sync.TradeCancelled -= OnCancelled;
            if (inputHandler != null && GodotObject.IsInstanceValid(inputHandler))
                inputHandler.QueueFree();
            _waitCts?.Dispose();
            _waitCts = null;
        }
    }

    private static async Task WaitForCancellation(CancellationToken token)
    {
        try
        {
            await Task.Delay(-1, token);
        }
        catch (TaskCanceledException) { }
    }

    private void ShowWaitingBubble(Player target)
    {
        try
        {
            var restSiteRoom = NRestSiteRoom.Instance;
            if (restSiteRoom == null) return;

            // Use the game's own method to find our character
            NRestSiteCharacter? myCharacter = restSiteRoom.GetCharacterForPlayer(Owner);
            if (myCharacter == null) return;

            string partnerName = PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, target.NetId);
            string waitText = $"Waiting for {partnerName}...";

            // Match the game's DialogueSide logic based on character index
            int charIdx = myCharacter._characterIndex;
            bool isRightSide = charIdx == 0 || charIdx == 3;
            var side = isRightSide ? DialogueSide.Right : DialogueSide.Left;

            _waitingBubble = NThoughtBubbleVfx.Create(waitText, side, null);
            if (_waitingBubble == null) return;

            // Add to scene tree first, then position at the character's thought bubble anchor
            myCharacter.AddChildSafely(_waitingBubble);
            _waitingBubble.GlobalPosition = myCharacter.GetRestSiteOptionAnchor().GlobalPosition;
            MainFile.Logger.Info($"Showing waiting bubble for partner {target.NetId}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to show waiting bubble: {e.Message}");
        }
    }

    private void RemoveWaitingBubble()
    {
        if (_waitingBubble != null && GodotObject.IsInstanceValid(_waitingBubble))
        {
            TaskHelper.RunSafely(_waitingBubble.GoAway());
        }
        _waitingBubble = null;
    }

    private async Task<bool> RunTradeUI()
    {
        var sync = TradeSynchronizer.Instance;
        if (sync?.ActiveSession == null)
        {
            MainFile.Logger.Error("RunTradeUI: ActiveSession is null, cannot open trade UI");
            return false;
        }

        MainFile.Logger.Info($"RunTradeUI: ActiveSession exists. Local={sync.ActiveSession.LocalPlayerId}, Partner={sync.ActiveSession.PartnerPlayerId}");

        _tradeCompletionSource = new TaskCompletionSource<bool>();

        // Filter by the active session's player IDs so that a different pair's
        // trade completing doesn't prematurely resolve our trade UI.
        var localId = sync.ActiveSession.LocalPlayerId;
        var partnerId = sync.ActiveSession.PartnerPlayerId;
        void OnCompleted(ulong pA, ulong pB)
        {
            if ((pA == localId && pB == partnerId) || (pA == partnerId && pB == localId))
                _tradeCompletionSource.TrySetResult(true);
        }
        void OnCancelled() => _tradeCompletionSource.TrySetResult(false);

        sync.TradeCompleted += OnCompleted;
        sync.TradeCancelled += OnCancelled;

        NTradeScreen? tradeScreen = null;
        try
        {
            tradeScreen = NTradeScreen.Create(sync);
            MainFile.Logger.Info($"RunTradeUI: NTradeScreen created. NRun.Instance={NRun.Instance != null}");

            // Try to find a valid parent for the trade screen
            Node? parent = null;
            if (NRun.Instance?.GlobalUi != null)
            {
                parent = NRun.Instance.GlobalUi;
                MainFile.Logger.Info("RunTradeUI: Using GlobalUi as parent");
            }
            else if (NRestSiteRoom.Instance != null)
            {
                parent = NRestSiteRoom.Instance;
                MainFile.Logger.Info("RunTradeUI: GlobalUi null, using NRestSiteRoom as parent");
            }
            else
            {
                MainFile.Logger.Error("RunTradeUI: No valid parent found for trade screen!");
                return false;
            }

            parent.AddChildSafely(tradeScreen);

            // Ensure the trade screen renders on top of other UI
            tradeScreen.ZIndex = 100;
            MainFile.Logger.Info("RunTradeUI: Trade screen added to scene tree, waiting for completion...");

            return await _tradeCompletionSource.Task;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"RunTradeUI: Exception: {e}");
            return false;
        }
        finally
        {
            sync.TradeCompleted -= OnCompleted;
            sync.TradeCancelled -= OnCancelled;
            if (tradeScreen != null && GodotObject.IsInstanceValid(tradeScreen))
            {
                tradeScreen.QueueFreeSafely();
            }
        }
    }

    private Player? NodeToPlayer(Node? node)
    {
        if (node is NRestSiteCharacter character)
            return character.Player;
        return null;
    }

    private bool ShouldCancelTargeting()
    {
        if (NOverlayStack.Instance.ScreenCount > 0) return true;
        if (NCapstoneContainer.Instance.InUse) return true;
        return false;
    }

    private bool AllowTargetingNode(Node node)
    {
        var player = NodeToPlayer(node);
        if (player == null) return false;
        if (LocalContext.IsMe(player)) return false;
        // Allow targeting all non-self players; post-selection validation
        // in OnSelectLocal handles "can't trade" with a thought bubble.
        return true;
    }

    /// <summary>
    /// Checks whether the selected target player can actually participate in a trade.
    /// Returns false if they've already traded or already used their rest site action.
    /// </summary>
    private bool CanTargetTrade(Player target, TradeSynchronizer sync)
    {
        // Already traded this rest site (unless unlimited trades is on)
        if (!TradeConfig.UnlimitedTrades && sync.PlayersWhoTraded.Contains(target.NetId))
        {
            MainFile.Logger.Info($"CanTargetTrade: {target.NetId} already traded");
            return false;
        }

        // Already acted (e.g., smithed/healed) and options were cleared
        // (unless they have MiniatureTent, in which case options remain)
        var restSiteSync = RunManager.Instance.RestSiteSynchronizer;
        var targetOptions = restSiteSync.GetOptionsForPlayer(target.NetId);
        if (targetOptions.Count == 0)
        {
            MainFile.Logger.Info($"CanTargetTrade: {target.NetId} has no remaining options (already acted)");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Shows a brief thought bubble on the local player's character
    /// indicating they can't trade with the selected target.
    /// </summary>
    private void ShowCantTradeBubble()
    {
        try
        {
            var restSiteRoom = NRestSiteRoom.Instance;
            if (restSiteRoom == null) return;

            NRestSiteCharacter? myCharacter = restSiteRoom.GetCharacterForPlayer(Owner);
            if (myCharacter == null) return;

            int charIdx = myCharacter._characterIndex;
            bool isRightSide = charIdx == 0 || charIdx == 3;
            var side = isRightSide ? DialogueSide.Right : DialogueSide.Left;

            var bubble = NThoughtBubbleVfx.Create("I can't trade with them.", side, 2.5);
            if (bubble == null) return;

            myCharacter.AddChildSafely(bubble);
            bubble.GlobalPosition = myCharacter.GetRestSiteOptionAnchor().GlobalPosition;
            MainFile.Logger.Info("Showing 'can't trade' bubble");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to show can't-trade bubble: {e.Message}");
        }
    }
}

/// <summary>
/// A full-screen transparent overlay that blocks all mouse input while waiting
/// for a trade partner, preventing the player from queueing other rest site
/// actions (e.g., spam-clicking Rest). Escape and right-click cancel the wait.
/// Uses _GuiInput for mouse events (blocked by MouseFilter.Stop) and _Input
/// for keyboard events (not blocked by MouseFilter).
/// </summary>
public partial class WaitingInputHandler : Control
{
    private Action? _onCancel;
    private bool _cancelled;

    public void Setup(Action onCancel)
    {
        _onCancel = onCancel;

        // Fill entire screen to block all mouse input below
        AnchorLeft = 0;
        AnchorTop = 0;
        AnchorRight = 1;
        AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_cancelled) return;

        if (inputEvent is InputEventKey keyEvent && keyEvent.IsPressed() && keyEvent.Keycode == Key.Escape)
        {
            _cancelled = true;
            _onCancel?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (_cancelled) return;

        // Right-click on the blocker cancels the wait
        if (inputEvent is InputEventMouseButton mouseEvent
            && mouseEvent.ButtonIndex == MouseButton.Right
            && mouseEvent.IsPressed())
        {
            _cancelled = true;
            _onCancel?.Invoke();
            AcceptEvent();
        }
        // Absorb all other mouse events (left clicks, etc.) to prevent
        // them from reaching rest site buttons underneath
        else if (inputEvent is InputEventMouseButton)
        {
            AcceptEvent();
        }
    }
}
