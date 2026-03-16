using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace STS2Trade.UI;

/// <summary>
/// Trade UI overlay screen. Uses Godot signals (Ready, Draw, TreeExiting) instead of
/// virtual method overrides (_Ready, _Draw, _ExitTree) because source generators are
/// disabled and Godot won't dispatch to overridden virtual methods without them.
/// </summary>
public partial class NTradeScreen : Control
{
    // Use game's StsColors palette
    private static readonly Color BgColor = new(0f, 0f, 0f, 0.8f); // StsColors.screenBackdrop
    private static readonly Color PanelColor = new(0.08f, 0.07f, 0.12f, 0.95f);
    private static readonly Color PanelBorderColor = new Color("EFC851"); // StsColors.gold
    private static readonly Color HeaderColor = new Color("EFC851"); // StsColors.gold
    private static readonly Color SubHeaderColor = new Color("FFF6E2"); // StsColors.cream
    private static readonly Color TextColor = new Color("FFF6E2"); // StsColors.cream
    private static readonly Color DimTextColor = new Color("FFF6E280"); // StsColors.halfTransparentCream
    private static readonly Color ConfirmColor = new Color("2AEBBE"); // StsColors.aqua
    private static readonly Color CancelColor = new Color("FF5555"); // StsColors.red
    private static readonly Color ConfirmedTextColor = new Color("2AEBBE"); // StsColors.aqua
    private static readonly Color ErrorTextColor = new Color("FF5555"); // StsColors.red

    private TradeSynchronizer _sync;
    private NTradeItemPicker? _itemPicker;
    private Godot.Timer? _inputPollTimer;

    private PanelContainer _mainPanel;
    private Label _headerLabel;
    private Label _statusLabel;
    private Label _validationErrorLabel;
    private Button _confirmButton;
    private Button _cancelButton;

    private VBoxContainer _localOfferContainer;
    private VBoxContainer _partnerOfferContainer;
    private Label _localConfirmIndicator;
    private Label _partnerConfirmIndicator;

    private List<NTradeSlot> _localCardSlots = new();
    private List<NTradeSlot> _localPotionSlots = new();
    private List<NTradeSlot> _localRelicSlots = new();
    private List<NTradeSlot> _partnerCardSlots = new();
    private List<NTradeSlot> _partnerPotionSlots = new();
    private List<NTradeSlot> _partnerRelicSlots = new();

    private bool _escWasPressed;

    public static NTradeScreen Create(TradeSynchronizer sync)
    {
        MainFile.Logger.Info("NTradeScreen.Create: Creating trade screen instance");
        var screen = new NTradeScreen();
        screen._sync = sync;

        // Use signals instead of virtual method overrides (source generators disabled)
        screen.Ready += screen.OnReady;
        screen.Draw += screen.OnDraw;
        screen.TreeExiting += screen.OnTreeExiting;

        return screen;
    }

    private void OnReady()
    {
        try
        {
            MainFile.Logger.Info("NTradeScreen.OnReady: Starting initialization");

            MouseFilter = MouseFilterEnum.Stop;

            // Fill the entire parent
            AnchorLeft = 0;
            AnchorTop = 0;
            AnchorRight = 1;
            AnchorBottom = 1;
            OffsetLeft = 0;
            OffsetTop = 0;
            OffsetRight = 0;
            OffsetBottom = 0;

            MainFile.Logger.Info("NTradeScreen.OnReady: Building UI...");
            BuildUI();
            MainFile.Logger.Info("NTradeScreen.OnReady: BuildUI complete, refreshing display...");
            RefreshDisplay();
            MainFile.Logger.Info("NTradeScreen.OnReady: RefreshDisplay complete");

            _sync.OfferUpdated += RefreshDisplay;
            _sync.ConfirmChanged += RefreshDisplay;
            _sync.TradeStateChanged += RefreshDisplay;

            // Timer to poll for Escape key (since _Input override doesn't work without source generators)
            _inputPollTimer = new Godot.Timer();
            _inputPollTimer.WaitTime = 0.05;
            _inputPollTimer.Autostart = true;
            _inputPollTimer.Timeout += PollEscapeKey;
            AddChild(_inputPollTimer);

            MainFile.Logger.Info($"NTradeScreen.OnReady: Finished. Size={Size}, Visible={Visible}, Parent={GetParent()?.Name}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeScreen.OnReady: EXCEPTION: {e}");
        }
    }

    private void OnTreeExiting()
    {
        if (_sync != null)
        {
            _sync.OfferUpdated -= RefreshDisplay;
            _sync.ConfirmChanged -= RefreshDisplay;
            _sync.TradeStateChanged -= RefreshDisplay;
        }
    }

    private void OnDraw()
    {
        // Semi-transparent dark backdrop over the entire screen
        DrawRect(new Rect2(Vector2.Zero, Size), BgColor);
    }

    private void PollEscapeKey()
    {
        bool escPressed = Input.IsKeyPressed(Key.Escape);
        if (escPressed && !_escWasPressed)
        {
            OnCancelPressed();
        }
        _escWasPressed = escPressed;
    }

    private void BuildUI()
    {
        var partnerPlayer = _sync.GetPartnerPlayer();
        string partnerName = partnerPlayer != null
            ? PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, partnerPlayer.NetId)
            : "Partner";

        // Main panel - centered using anchors
        _mainPanel = new PanelContainer();
        _mainPanel.AnchorLeft = 0.5f;
        _mainPanel.AnchorTop = 0.5f;
        _mainPanel.AnchorRight = 0.5f;
        _mainPanel.AnchorBottom = 0.5f;
        _mainPanel.OffsetLeft = -550;
        _mainPanel.OffsetTop = -350;
        _mainPanel.OffsetRight = 550;
        _mainPanel.OffsetBottom = 350;
        _mainPanel.GrowHorizontal = GrowDirection.Both;
        _mainPanel.GrowVertical = GrowDirection.Both;

        // Style the panel
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = PanelColor;
        panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthLeft = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderColor = PanelBorderColor;
        panelStyle.CornerRadiusBottomLeft = 8;
        panelStyle.CornerRadiusBottomRight = 8;
        panelStyle.CornerRadiusTopLeft = 8;
        panelStyle.CornerRadiusTopRight = 8;
        panelStyle.ContentMarginLeft = 30;
        panelStyle.ContentMarginRight = 30;
        panelStyle.ContentMarginTop = 20;
        panelStyle.ContentMarginBottom = 20;
        _mainPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_mainPanel);

        // Root layout inside panel
        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 10);
        _mainPanel.AddChild(rootVBox);

        // Header
        _headerLabel = new Label();
        _headerLabel.Text = $"Trading with {partnerName}";
        _headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _headerLabel.AddThemeColorOverride("font_color", HeaderColor);
        _headerLabel.AddThemeFontSizeOverride("font_size", 32);
        rootVBox.AddChild(_headerLabel);

        // Separator under header
        var headerSep = new HSeparator();
        headerSep.AddThemeConstantOverride("separation", 4);
        rootVBox.AddChild(headerSep);

        // Two-column layout
        var columnsContainer = new HBoxContainer();
        columnsContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        columnsContainer.AddThemeConstantOverride("separation", 20);
        rootVBox.AddChild(columnsContainer);

        _localOfferContainer = BuildOfferColumn("Your Offer", true);
        _localOfferContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columnsContainer.AddChild(_localOfferContainer);

        // Vertical divider
        var divider = new VSeparator();
        columnsContainer.AddChild(divider);

        _partnerOfferContainer = BuildOfferColumn($"{partnerName}'s Offer", false);
        _partnerOfferContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columnsContainer.AddChild(_partnerOfferContainer);

        // Bottom separator
        var bottomSep = new HSeparator();
        bottomSep.AddThemeConstantOverride("separation", 4);
        rootVBox.AddChild(bottomSep);

        // Validation error label (always visible when there's an error, above buttons)
        _validationErrorLabel = new Label();
        _validationErrorLabel.Text = "";
        _validationErrorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _validationErrorLabel.AddThemeColorOverride("font_color", ErrorTextColor);
        _validationErrorLabel.AddThemeFontSizeOverride("font_size", 18);
        _validationErrorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rootVBox.AddChild(_validationErrorLabel);

        // Bottom bar with buttons and status
        var bottomBar = new HBoxContainer();
        bottomBar.AddThemeConstantOverride("separation", 12);
        rootVBox.AddChild(bottomBar);

        _cancelButton = CreateStyledButton("Cancel", CancelColor);
        _cancelButton.Pressed += OnCancelPressed;
        bottomBar.AddChild(_cancelButton);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomBar.AddChild(spacer);

        _statusLabel = new Label();
        _statusLabel.Text = "Select items to trade";
        _statusLabel.AddThemeColorOverride("font_color", DimTextColor);
        _statusLabel.AddThemeFontSizeOverride("font_size", 20);
        _statusLabel.VerticalAlignment = VerticalAlignment.Center;
        bottomBar.AddChild(_statusLabel);

        var spacer2 = new Control();
        spacer2.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomBar.AddChild(spacer2);

        _confirmButton = CreateStyledButton("Confirm", ConfirmColor);
        _confirmButton.Pressed += OnConfirmPressed;
        bottomBar.AddChild(_confirmButton);
    }

    private Button CreateStyledButton(string text, Color fontColor)
    {
        var button = new Button();
        button.Text = text;
        button.CustomMinimumSize = new Vector2(160, 48);

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
        normalStyle.BorderWidthBottom = 2;
        normalStyle.BorderWidthTop = 2;
        normalStyle.BorderWidthLeft = 2;
        normalStyle.BorderWidthRight = 2;
        normalStyle.BorderColor = fontColor * new Color(1, 1, 1, 0.5f);
        normalStyle.CornerRadiusBottomLeft = 6;
        normalStyle.CornerRadiusBottomRight = 6;
        normalStyle.CornerRadiusTopLeft = 6;
        normalStyle.CornerRadiusTopRight = 6;
        normalStyle.ContentMarginLeft = 16;
        normalStyle.ContentMarginRight = 16;
        normalStyle.ContentMarginTop = 8;
        normalStyle.ContentMarginBottom = 8;

        var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
        hoverStyle!.BgColor = new Color(0.2f, 0.2f, 0.28f, 0.95f);
        hoverStyle.BorderColor = fontColor;

        var pressedStyle = normalStyle.Duplicate() as StyleBoxFlat;
        pressedStyle!.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);

        var disabledStyle = normalStyle.Duplicate() as StyleBoxFlat;
        disabledStyle!.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        disabledStyle.BorderColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

        button.AddThemeStyleboxOverride("normal", normalStyle);
        button.AddThemeStyleboxOverride("hover", hoverStyle);
        button.AddThemeStyleboxOverride("pressed", pressedStyle);
        button.AddThemeStyleboxOverride("disabled", disabledStyle);
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeColorOverride("font_hover_color", fontColor);
        button.AddThemeColorOverride("font_pressed_color", fontColor * new Color(1, 1, 1, 0.7f));
        button.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.4f, 0.4f));
        button.AddThemeFontSizeOverride("font_size", 22);

        return button;
    }

    private VBoxContainer BuildOfferColumn(string title, bool isLocal)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);

        var titleLabel = new Label();
        titleLabel.Text = title;
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeColorOverride("font_color", SubHeaderColor);
        titleLabel.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(titleLabel);

        if (isLocal)
        {
            _localConfirmIndicator = new Label();
            _localConfirmIndicator.HorizontalAlignment = HorizontalAlignment.Center;
            _localConfirmIndicator.AddThemeFontSizeOverride("font_size", 16);
            column.AddChild(_localConfirmIndicator);
        }
        else
        {
            _partnerConfirmIndicator = new Label();
            _partnerConfirmIndicator.HorizontalAlignment = HorizontalAlignment.Center;
            _partnerConfirmIndicator.AddThemeFontSizeOverride("font_size", 16);
            column.AddChild(_partnerConfirmIndicator);
        }

        BuildSlotSection(column, "Cards", TradeConfig.MaxCardSlots, isLocal,
            isLocal ? _localCardSlots : _partnerCardSlots, NTradeSlot.SlotType.Card);

        BuildSlotSection(column, "Potions", TradeConfig.MaxPotionSlots, isLocal,
            isLocal ? _localPotionSlots : _partnerPotionSlots, NTradeSlot.SlotType.Potion);

        BuildSlotSection(column, "Relics", TradeConfig.MaxRelicSlots, isLocal,
            isLocal ? _localRelicSlots : _partnerRelicSlots, NTradeSlot.SlotType.Relic);

        return column;
    }

    private void BuildSlotSection(VBoxContainer parent, string label, int maxSlots, bool isLocal,
        List<NTradeSlot> slotList, NTradeSlot.SlotType slotType)
    {
        // Header row: label on the left, view button on the right (partner only)
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);

        var sectionLabel = new Label();
        sectionLabel.Text = $"{label} (0/{maxSlots})";
        sectionLabel.AddThemeColorOverride("font_color", DimTextColor);
        sectionLabel.AddThemeFontSizeOverride("font_size", 18);
        sectionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(sectionLabel);

        if (!isLocal)
        {
            var viewBtn = CreateViewButton(slotType);
            headerRow.AddChild(viewBtn);
        }

        parent.AddChild(headerRow);

        var slotsContainer = new HBoxContainer();
        slotsContainer.AddThemeConstantOverride("separation", 8);
        parent.AddChild(slotsContainer);

        for (int i = 0; i < maxSlots; i++)
        {
            var slot = NTradeSlot.Create(slotType, isLocal, i);
            if (isLocal)
            {
                int slotIndex = i;
                slot.SlotClicked += () => OnLocalSlotClicked(slotType, slotIndex);
            }
            slotsContainer.AddChild(slot);
            slotList.Add(slot);
        }
    }

    private Button CreateViewButton(NTradeSlot.SlotType slotType)
    {
        var btn = new Button();
        btn.Text = "View";
        btn.CustomMinimumSize = new Vector2(60, 24);
        btn.AddThemeColorOverride("font_color", DimTextColor);
        btn.AddThemeColorOverride("font_hover_color", new Color("EFC851"));
        btn.AddThemeFontSizeOverride("font_size", 14);

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0, 0, 0, 0);
        normalStyle.BorderWidthBottom = 1;
        normalStyle.BorderWidthTop = 1;
        normalStyle.BorderWidthLeft = 1;
        normalStyle.BorderWidthRight = 1;
        normalStyle.BorderColor = DimTextColor * new Color(1, 1, 1, 0.4f);
        normalStyle.CornerRadiusBottomLeft = 4;
        normalStyle.CornerRadiusBottomRight = 4;
        normalStyle.CornerRadiusTopLeft = 4;
        normalStyle.CornerRadiusTopRight = 4;
        normalStyle.ContentMarginLeft = 8;
        normalStyle.ContentMarginRight = 8;
        normalStyle.ContentMarginTop = 2;
        normalStyle.ContentMarginBottom = 2;

        var hoverStyle = (StyleBoxFlat)normalStyle.Duplicate();
        hoverStyle.BorderColor = new Color("EFC851");

        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", normalStyle);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        btn.Pressed += () => ShowPartnerItemView(slotType);
        return btn;
    }

    private void ShowPartnerItemView(NTradeSlot.SlotType slotType)
    {
        if (_sync.ActiveSession == null) return;

        var partnerPlayer = _sync.GetPartnerPlayer();
        if (partnerPlayer == null) return;

        // If a view picker is already open, close it (toggle off)
        if (_itemPicker != null)
        {
            _itemPicker.QueueFree();
            _itemPicker = null;
            return;
        }

        // Reuse NTradeItemPicker in view-only mode
        _itemPicker = NTradeItemPicker.CreateViewOnly(slotType, partnerPlayer);
        _itemPicker.Cancelled += () =>
        {
            _itemPicker?.QueueFree();
            _itemPicker = null;
        };
        AddChild(_itemPicker);
    }

    private void OnLocalSlotClicked(NTradeSlot.SlotType type, int slotIndex)
    {
        if (_sync.ActiveSession == null) return;
        if (_sync.ActiveSession.LocalConfirmed) return;

        var offer = _sync.ActiveSession.LocalOffer;

        List<int> targetList = type switch
        {
            NTradeSlot.SlotType.Card => offer.CardDeckIndices,
            NTradeSlot.SlotType.Potion => offer.PotionSlotIndices,
            NTradeSlot.SlotType.Relic => offer.RelicIndices,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (slotIndex < targetList.Count)
        {
            // Remove item from this slot
            targetList.RemoveAt(slotIndex);
            _sync.UpdateLocalOffer(offer);
            RefreshDisplay();
        }
        else
        {
            // Use custom picker for cards, potions, and relics
            // (NOT the native CardSelectCmd which uses PlayerChoiceSynchronizer
            // and would create synced choice IDs that the remote machine never consumes)
            ShowItemPicker(type);
        }
    }

    private void ShowItemPicker(NTradeSlot.SlotType type)
    {
        _itemPicker?.QueueFree();

        var localPlayer = _sync.GetLocalPlayer();
        if (localPlayer == null) return;

        _itemPicker = NTradeItemPicker.Create(type, localPlayer, _sync.ActiveSession!.LocalOffer);
        _itemPicker.ItemSelected += (index) => OnItemSelected(type, index);
        _itemPicker.Cancelled += () =>
        {
            _itemPicker?.QueueFree();
            _itemPicker = null;
        };
        AddChild(_itemPicker);
    }

    private void OnItemSelected(NTradeSlot.SlotType type, int itemIndex)
    {
        if (_sync.ActiveSession == null) return;

        var offer = _sync.ActiveSession.LocalOffer;
        int maxSlots = type switch
        {
            NTradeSlot.SlotType.Card => TradeConfig.MaxCardSlots,
            NTradeSlot.SlotType.Potion => TradeConfig.MaxPotionSlots,
            NTradeSlot.SlotType.Relic => TradeConfig.MaxRelicSlots,
            _ => 0
        };

        List<int> targetList = type switch
        {
            NTradeSlot.SlotType.Card => offer.CardDeckIndices,
            NTradeSlot.SlotType.Potion => offer.PotionSlotIndices,
            NTradeSlot.SlotType.Relic => offer.RelicIndices,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (targetList.Count >= maxSlots) return;
        if (targetList.Contains(itemIndex)) return;

        targetList.Add(itemIndex);
        _sync.UpdateLocalOffer(offer);

        _itemPicker?.QueueFree();
        _itemPicker = null;

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        try
        {
            if (_sync.ActiveSession == null) return;
            if (_confirmButton == null) return; // UI not built yet

            var localPlayer = _sync.GetLocalPlayer();
            var partnerPlayer = _sync.GetPartnerPlayer();
            if (localPlayer == null || partnerPlayer == null) return;

            var session = _sync.ActiveSession;

            RefreshCardSlots(_localCardSlots, session.LocalOffer.CardDeckIndices, localPlayer);
            RefreshPotionSlots(_localPotionSlots, session.LocalOffer.PotionSlotIndices, localPlayer);
            RefreshRelicSlots(_localRelicSlots, session.LocalOffer.RelicIndices, localPlayer);

            RefreshCardSlots(_partnerCardSlots, session.PartnerOffer.CardDeckIndices, partnerPlayer);
            RefreshPotionSlots(_partnerPotionSlots, session.PartnerOffer.PotionSlotIndices, partnerPlayer);
            RefreshRelicSlots(_partnerRelicSlots, session.PartnerOffer.RelicIndices, partnerPlayer);

            if (_localConfirmIndicator != null)
            {
                _localConfirmIndicator.Text = session.LocalConfirmed ? "CONFIRMED" : "";
                _localConfirmIndicator.AddThemeColorOverride("font_color", ConfirmedTextColor);
            }
            if (_partnerConfirmIndicator != null)
            {
                _partnerConfirmIndicator.Text = session.PartnerConfirmed ? "CONFIRMED" : "";
                _partnerConfirmIndicator.AddThemeColorOverride("font_color", ConfirmedTextColor);
            }

            // Allow clicking when confirmed (to unconfirm/toggle).
            // Only disable when not confirmed AND validation fails.
            if (session.LocalConfirmed)
            {
                _confirmButton.Disabled = false;
                _confirmButton.Text = "Confirmed! (click to undo)";
            }
            else
            {
                bool canConfirm = session.ValidateCanConfirm(localPlayer, partnerPlayer);
                _confirmButton.Disabled = !canConfirm;
                _confirmButton.Text = "Confirm";
            }

            // Show validation error as a persistent label above the buttons
            string? validationError = null;
            if (!session.LocalConfirmed)
                validationError = session.GetValidationError(localPlayer, partnerPlayer);

            if (_validationErrorLabel != null)
            {
                _validationErrorLabel.Text = validationError ?? "";
                _validationErrorLabel.Visible = validationError != null;
            }

            if (session.BothConfirmed)
                _statusLabel.Text = "Trade complete!";
            else if (session.LocalConfirmed && !session.PartnerConfirmed)
                _statusLabel.Text = "Waiting for partner to confirm...";
            else if (!session.LocalConfirmed && session.PartnerConfirmed)
                _statusLabel.Text = "Partner has confirmed. Review and confirm.";
            else
                _statusLabel.Text = "Select items to trade";
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeScreen.RefreshDisplay: EXCEPTION: {e}");
        }
    }

    private void RefreshCardSlots(List<NTradeSlot> slots, List<int> indices, Player player)
    {
        var cards = player.Deck?.Cards;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i < indices.Count && cards != null)
            {
                int idx = indices[i];
                if (idx >= 0 && idx < cards.Count)
                {
                    var card = cards[idx];
                    string name = card.Title ?? card.Id.Entry;
                    slots[i].SetItem(name, cardModel: card);
                }
                else
                {
                    slots[i].SetItem("???");
                }
            }
            else
            {
                slots[i].ClearItem();
            }
        }
    }

    private void RefreshPotionSlots(List<NTradeSlot> slots, List<int> indices, Player player)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (i < indices.Count)
            {
                int idx = indices[i];
                if (idx >= 0 && idx < player.PotionSlots.Count && player.PotionSlots[idx] != null)
                {
                    var potion = player.PotionSlots[idx]!;
                    string name = GetPotionName(potion);
                    IEnumerable<IHoverTip>? tips = null;
                    try { tips = potion.HoverTips; } catch { /* ignore */ }
                    slots[i].SetItem(name, tips);
                }
                else
                {
                    slots[i].SetItem("???");
                }
            }
            else
            {
                slots[i].ClearItem();
            }
        }
    }

    private void RefreshRelicSlots(List<NTradeSlot> slots, List<int> indices, Player player)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (i < indices.Count)
            {
                int idx = indices[i];
                if (idx >= 0 && idx < player.Relics.Count)
                {
                    var relic = player.Relics[idx];
                    string name = GetRelicName(relic);
                    IEnumerable<IHoverTip>? tips = null;
                    try { tips = relic.HoverTips; } catch { /* ignore */ }
                    slots[i].SetItem(name, tips);
                }
                else
                {
                    slots[i].SetItem("???");
                }
            }
            else
            {
                slots[i].ClearItem();
            }
        }
    }

    private void OnConfirmPressed()
    {
        if (_sync.ActiveSession == null) return;

        if (_sync.ActiveSession.LocalConfirmed)
        {
            _sync.SetLocalConfirmed(false);
        }
        else
        {
            _sync.SetLocalConfirmed(true);
        }
    }

    private void OnCancelPressed()
    {
        _sync.CancelTrade();
    }

    private static string GetPotionName(PotionModel? potion)
    {
        if (potion == null) return "";
        try { return potion.Title.GetFormattedText(); }
        catch { return potion.Id.Entry; }
    }

    private static string GetRelicName(RelicModel? relic)
    {
        if (relic == null) return "";
        try { return relic.Title.GetFormattedText(); }
        catch { return relic.Id.Entry; }
    }

}
