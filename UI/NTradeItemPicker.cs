using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace STS2Trade.UI;

/// <summary>
/// Item picker for cards, potions, and relics with hover highlights, localized names,
/// and hover tooltips showing item descriptions.
/// Supports a view-only mode for browsing a partner's items without selection.
/// Uses an overlay button approach: visual content (NPotion/NRelic) is rendered behind
/// a semi-transparent button that catches all clicks, preventing child node interference.
/// </summary>
public partial class NTradeItemPicker : Control
{
    private static readonly Color OverlayColor = new(0f, 0f, 0f, 0.7f);
    private static readonly Color PanelColor = new(0.08f, 0.07f, 0.12f, 0.95f);
    private static readonly Color BorderColor = new Color("EFC851");
    private static readonly Color HeaderColor = new Color("EFC851");
    private static readonly Color CancelColor = new Color("FF5555");
    private static readonly Color DimColor = new Color("FFF6E280");
    private static readonly Color ItemBorderNormal = new Color("EFC851") * new Color(1, 1, 1, 0.15f);
    private static readonly Color ItemBorderHover = new Color("EFC851");

    private NTradeSlot.SlotType _type;
    private Player _player;
    private TradeOffer? _currentOffer;
    private bool _viewOnly;

    public event Action<int>? ItemSelected;
    public event Action? Cancelled;

    /// <summary>
    /// Creates a picker for selecting items to trade.
    /// </summary>
    public static NTradeItemPicker Create(NTradeSlot.SlotType type, Player player, TradeOffer currentOffer)
    {
        var picker = new NTradeItemPicker();
        picker._type = type;
        picker._player = player;
        picker._currentOffer = currentOffer;
        picker._viewOnly = false;
        return picker;
    }

    /// <summary>
    /// Creates a read-only picker for viewing a player's items without selection.
    /// </summary>
    public static NTradeItemPicker CreateViewOnly(NTradeSlot.SlotType type, Player player)
    {
        var picker = new NTradeItemPicker();
        picker._type = type;
        picker._player = player;
        picker._currentOffer = null;
        picker._viewOnly = true;
        return picker;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 0;
        AnchorTop = 0;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = 0;
        OffsetTop = 0;
        OffsetRight = 0;
        OffsetBottom = 0;
        BuildUI();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), OverlayColor);
    }

    private void BuildUI()
    {
        switch (_type)
        {
            case NTradeSlot.SlotType.Card:
                BuildCardPicker();
                break;
            case NTradeSlot.SlotType.Potion:
                BuildPotionPicker();
                break;
            case NTradeSlot.SlotType.Relic:
                BuildRelicPicker();
                break;
        }
        AddCancelButton();
    }

    /// <summary>
    /// Creates an item entry with a visual content layer and a clickable overlay button on top.
    /// The button is a SIBLING of the content (not a parent), so NPotion/NRelic child nodes
    /// cannot intercept mouse events regardless of their MouseFilter settings.
    /// In view-only mode, items are not clickable but still show hover tooltips.
    /// </summary>
    private Control CreateItemEntry(Control? iconNode, string name, bool alreadyOffered, int index,
        Vector2 minSize, int fontSize = 12, bool blocked = false,
        CardModel? cardModel = null, IEnumerable<IHoverTip>? hoverTips = null)
    {
        var wrapper = new Control();
        wrapper.CustomMinimumSize = minSize;

        // === Content layer (rendered first = behind) ===
        var vbox = new VBoxContainer();
        vbox.AnchorLeft = 0;
        vbox.AnchorTop = 0;
        vbox.AnchorRight = 1;
        vbox.AnchorBottom = 1;
        vbox.OffsetLeft = 8;
        vbox.OffsetTop = 12;   // More space at top for icon
        vbox.OffsetRight = -8;
        vbox.OffsetBottom = -4; // Less space at bottom for text
        vbox.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddThemeConstantOverride("separation", 4);

        if (iconNode != null)
        {
            iconNode.MouseFilter = MouseFilterEnum.Ignore;
            iconNode.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            vbox.AddChild(iconNode);
        }

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var labelColor = blocked ? new Color("FF6666") : alreadyOffered ? DimColor : new Color("FFF6E2");
        nameLabel.AddThemeColorOverride("font_color", labelColor);
        nameLabel.AddThemeFontSizeOverride("font_size", fontSize);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        nameLabel.CustomMinimumSize = new Vector2(minSize.X - 16, 0);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        vbox.AddChild(nameLabel);

        wrapper.AddChild(vbox);

        // === Click overlay (rendered last = on top, catches ALL clicks) ===
        var button = CreateOverlayButton();
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        if (blocked)
        {
            // Blocked items are always dimmed, in both select and view modes
            button.Disabled = true;
            wrapper.Modulate = new Color(1, 1, 1, 0.35f);
        }
        else if (alreadyOffered && !_viewOnly)
        {
            button.Disabled = true;
            wrapper.Modulate = new Color(1, 1, 1, 0.35f);
        }

        if (!_viewOnly && !blocked)
        {
            int idx = index;
            button.Pressed += () =>
            {
                MainFile.Logger.Info($"NTradeItemPicker: Item clicked, index={idx}");
                ItemSelected?.Invoke(idx);
            };
        }

        // === Hover tooltip ===
        var capturedCardModel = cardModel;
        var capturedHoverTips = hoverTips;
        button.MouseEntered += () => ShowTooltip(capturedCardModel, capturedHoverTips);
        button.MouseExited += HideTooltip;

        wrapper.AddChild(button);

        return wrapper;
    }

    private void ShowTooltip(CardModel? cardModel, IEnumerable<IHoverTip>? hoverTips)
    {
        try
        {
            var tradeScreen = FindTradeScreenParent();
            if (tradeScreen == null) return;

            var tooltip = NTradeTooltip.GetOrCreate(tradeScreen);

            if (cardModel != null)
            {
                tooltip.ShowForCard(cardModel, this);
            }
            else if (hoverTips != null)
            {
                tooltip.ShowForHoverTips(hoverTips, this);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeItemPicker: Failed to show tooltip: {e.Message}");
        }
    }

    private void HideTooltip()
    {
        try
        {
            var tradeScreen = FindTradeScreenParent();
            if (tradeScreen == null) return;
            var tooltip = NTradeTooltip.GetOrCreate(tradeScreen);
            tooltip.Hide();
        }
        catch { /* ignore */ }
    }

    private NTradeScreen? FindTradeScreenParent()
    {
        Node? current = GetParent();
        while (current != null)
        {
            if (current is NTradeScreen screen) return screen;
            current = current.GetParent();
        }
        return null;
    }

    /// <summary>
    /// Creates a semi-transparent overlay button with gold border on hover.
    /// Background is transparent so content behind is visible.
    /// </summary>
    private Button CreateOverlayButton()
    {
        var button = new Button();

        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0, 0, 0, 0); // Fully transparent
        normalStyle.BorderWidthBottom = 2;
        normalStyle.BorderWidthTop = 2;
        normalStyle.BorderWidthLeft = 2;
        normalStyle.BorderWidthRight = 2;
        normalStyle.BorderColor = ItemBorderNormal;
        normalStyle.CornerRadiusBottomLeft = 6;
        normalStyle.CornerRadiusBottomRight = 6;
        normalStyle.CornerRadiusTopLeft = 6;
        normalStyle.CornerRadiusTopRight = 6;

        var hoverStyle = (StyleBoxFlat)normalStyle.Duplicate();
        hoverStyle.BgColor = new Color(0, 0, 0, 0); // Fully transparent — don't darken the card
        hoverStyle.BorderColor = ItemBorderHover; // Full gold
        hoverStyle.BorderWidthBottom = 3;
        hoverStyle.BorderWidthTop = 3;
        hoverStyle.BorderWidthLeft = 3;
        hoverStyle.BorderWidthRight = 3;

        var pressedStyle = (StyleBoxFlat)normalStyle.Duplicate();
        pressedStyle.BgColor = new Color(0, 0, 0, 0); // Fully transparent
        pressedStyle.BorderColor = ItemBorderHover * new Color(1, 1, 1, 0.7f);
        pressedStyle.BorderWidthBottom = 3;
        pressedStyle.BorderWidthTop = 3;
        pressedStyle.BorderWidthLeft = 3;
        pressedStyle.BorderWidthRight = 3;

        var disabledStyle = (StyleBoxFlat)normalStyle.Duplicate();
        disabledStyle.BgColor = new Color(0, 0, 0, 0);
        disabledStyle.BorderColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

        button.AddThemeStyleboxOverride("normal", normalStyle);
        button.AddThemeStyleboxOverride("hover", hoverStyle);
        button.AddThemeStyleboxOverride("pressed", pressedStyle);
        button.AddThemeStyleboxOverride("disabled", disabledStyle);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        return button;
    }

    /// <summary>
    /// Card scale factor — matches the game's grid card scale (NCardGrid uses 0.8,
    /// but we use a slightly smaller scale to fit more cards in the picker panel).
    /// NCard.defaultSize is 300x422, so at 0.55 scale each card is ~165x232.
    /// </summary>
    private const float CardScale = 0.55f;
    private const float CardPadding = 8f; // Padding around card for highlight border
    private static readonly Vector2 CardVisualSize = NCard.defaultSize * CardScale;
    private static readonly Vector2 CardCellSize = CardVisualSize + new Vector2(CardPadding * 2, CardPadding * 2);

    private void BuildCardPicker()
    {
        string headerText = _viewOnly ? "Viewing Deck" : "Select a Card";

        // Use near-fullscreen layout to maximize card grid space
        var containerPanel = new PanelContainer();
        containerPanel.AnchorLeft = 0.03f;
        containerPanel.AnchorTop = 0.03f;
        containerPanel.AnchorRight = 0.97f;
        containerPanel.AnchorBottom = 0.88f;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = PanelColor;
        panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthLeft = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderColor = BorderColor;
        panelStyle.CornerRadiusBottomLeft = 8;
        panelStyle.CornerRadiusBottomRight = 8;
        panelStyle.CornerRadiusTopLeft = 8;
        panelStyle.CornerRadiusTopRight = 8;
        panelStyle.ContentMarginLeft = 20;
        panelStyle.ContentMarginRight = 20;
        panelStyle.ContentMarginTop = 15;
        panelStyle.ContentMarginBottom = 15;
        containerPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(containerPanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        containerPanel.AddChild(vbox);

        var header = new Label();
        header.Text = headerText;
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeColorOverride("font_color", HeaderColor);
        header.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(header);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.ClipContents = true;
        vbox.AddChild(scrollContainer);

        var gridContainer = new GridContainer();
        gridContainer.Columns = 9;
        gridContainer.AddThemeConstantOverride("h_separation", 6);
        gridContainer.AddThemeConstantOverride("v_separation", 6);
        gridContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        scrollContainer.AddChild(gridContainer);

        CardPile? deck = _player.Deck;
        if (deck == null) return;

        for (int i = 0; i < deck.Cards.Count; i++)
        {
            var card = deck.Cards[i];
            bool alreadyOffered = !_viewOnly && _currentOffer != null && _currentOffer.CardDeckIndices.Contains(i);
            bool blockedQuest = TradeConfig.BlockQuestCards && card.Type == CardType.Quest;

            var (entry, nCard) = CreateCardEntry(card, alreadyOffered, i, blocked: blockedQuest);
            gridContainer.AddChild(entry);

            // UpdateVisuals must be called AFTER the card enters the scene tree.
            nCard?.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
        }
    }

    /// <summary>
    /// Creates a card entry using the game's NCard visual with an overlay button for clicks.
    /// The NCard renders the full card art, and the overlay button on top handles selection.
    /// Returns both the wrapper Control and the NCard reference (so caller can call UpdateVisuals).
    /// </summary>
    private (Control entry, NCard? nCard) CreateCardEntry(CardModel card, bool alreadyOffered, int deckIndex, bool blocked = false)
    {
        // Wrapper sized to the scaled card dimensions
        var wrapper = new Control();
        wrapper.CustomMinimumSize = CardCellSize;

        NCard? createdCard = null;

        // === NCard visual (rendered behind overlay) ===
        try
        {
            var nCard = NCard.Create(card);
            if (nCard != null)
            {
                createdCard = nCard;
                nCard.Scale = new Vector2(CardScale, CardScale);
                nCard.Position = CardVisualSize * 0.5f + new Vector2(CardPadding, CardPadding);
                nCard.MouseFilter = MouseFilterEnum.Ignore;
                wrapper.AddChild(nCard);
                DisableMouseRecursive(nCard);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to create NCard for {card.Id.Entry}: {e.Message}");
            var fallbackLabel = new Label();
            fallbackLabel.Text = !string.IsNullOrEmpty(card.Title) ? card.Title : card.Id.Entry;
            fallbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
            fallbackLabel.VerticalAlignment = VerticalAlignment.Center;
            fallbackLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            fallbackLabel.AddThemeColorOverride("font_color", new Color("FFF6E2"));
            fallbackLabel.AddThemeFontSizeOverride("font_size", 13);
            fallbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            fallbackLabel.MouseFilter = MouseFilterEnum.Ignore;
            wrapper.AddChild(fallbackLabel);
        }

        // === Click overlay (rendered on top, catches ALL clicks) ===
        var button = CreateOverlayButton();
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        if (blocked)
        {
            // Blocked cards (e.g. quest) are always dimmed, in both select and view modes
            button.Disabled = true;
            wrapper.Modulate = new Color(1, 1, 1, 0.35f);
        }
        else if (alreadyOffered && !_viewOnly)
        {
            button.Disabled = true;
            wrapper.Modulate = new Color(1, 1, 1, 0.35f);
        }

        if (!_viewOnly && !blocked)
        {
            int idx = deckIndex;
            button.Pressed += () =>
            {
                MainFile.Logger.Info($"NTradeItemPicker: Card clicked, deckIndex={idx}");
                ItemSelected?.Invoke(idx);
            };
        }

        wrapper.AddChild(button);

        return (wrapper, createdCard);
    }

    /// <summary>
    /// Recursively sets MouseFilter to Ignore on a node and all its children,
    /// so the overlay button above can capture all mouse events.
    /// </summary>
    private static void DisableMouseRecursive(Node node)
    {
        if (node is Control control)
            control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren())
            DisableMouseRecursive(child);
    }

    private void BuildPotionPicker()
    {
        string headerText = _viewOnly ? "Viewing Potions" : "Select a Potion";

        var containerPanel = new PanelContainer();
        containerPanel.AnchorLeft = 0.5f;
        containerPanel.AnchorTop = 0.5f;
        containerPanel.AnchorRight = 0.5f;
        containerPanel.AnchorBottom = 0.5f;
        containerPanel.OffsetLeft = -400;
        containerPanel.OffsetTop = -220;
        containerPanel.OffsetRight = 400;
        containerPanel.OffsetBottom = 220;
        containerPanel.GrowHorizontal = GrowDirection.Both;
        containerPanel.GrowVertical = GrowDirection.Both;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = PanelColor;
        panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthLeft = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderColor = BorderColor;
        panelStyle.CornerRadiusBottomLeft = 8;
        panelStyle.CornerRadiusBottomRight = 8;
        panelStyle.CornerRadiusTopLeft = 8;
        panelStyle.CornerRadiusTopRight = 8;
        panelStyle.ContentMarginLeft = 20;
        panelStyle.ContentMarginRight = 20;
        panelStyle.ContentMarginTop = 20;
        panelStyle.ContentMarginBottom = 20;
        containerPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(containerPanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 15);
        containerPanel.AddChild(vbox);

        var header = new Label();
        header.Text = headerText;
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeColorOverride("font_color", HeaderColor);
        header.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(header);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var potionRow = new HBoxContainer();
        potionRow.AddThemeConstantOverride("separation", 20);
        potionRow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        vbox.AddChild(potionRow);

        bool anyPotions = false;
        for (int i = 0; i < _player.PotionSlots.Count; i++)
        {
            var potion = _player.PotionSlots[i];
            if (potion == null) continue;
            anyPotions = true;

            bool alreadyOffered = !_viewOnly && _currentOffer != null && _currentOffer.PotionSlotIndices.Contains(i);

            // Create potion icon
            Control? iconNode = null;
            try
            {
                var nPotion = NPotion.Create(potion);
                if (nPotion != null)
                {
                    nPotion.CustomMinimumSize = new Vector2(64, 64);
                    iconNode = nPotion;
                }
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Failed to create potion display: {e.Message}");
            }

            // Localized name
            string potionName;
            try { potionName = potion.Title.GetFormattedText(); }
            catch { potionName = potion.Id.Entry; }

            // Get hover tips for tooltip
            IEnumerable<IHoverTip>? tips = null;
            try { tips = potion.HoverTips; }
            catch { /* ignore */ }

            var entry = CreateItemEntry(iconNode, potionName, alreadyOffered, i, new Vector2(110, 130),
                hoverTips: tips);
            potionRow.AddChild(entry);
        }

        if (!anyPotions)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = "No potions";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.AddThemeColorOverride("font_color", DimColor);
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            vbox.AddChild(emptyLabel);
        }
    }

    private void BuildRelicPicker()
    {
        string headerText = _viewOnly ? "Viewing Relics" : "Select a Relic";

        var containerPanel = new PanelContainer();
        containerPanel.AnchorLeft = 0.1f;
        containerPanel.AnchorTop = 0.1f;
        containerPanel.AnchorRight = 0.9f;
        containerPanel.AnchorBottom = 0.85f;
        containerPanel.OffsetLeft = 0;
        containerPanel.OffsetTop = 0;
        containerPanel.OffsetRight = 0;
        containerPanel.OffsetBottom = 0;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = PanelColor;
        panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthLeft = 2;
        panelStyle.BorderWidthRight = 2;
        panelStyle.BorderColor = BorderColor;
        panelStyle.CornerRadiusBottomLeft = 8;
        panelStyle.CornerRadiusBottomRight = 8;
        panelStyle.CornerRadiusTopLeft = 8;
        panelStyle.CornerRadiusTopRight = 8;
        panelStyle.ContentMarginLeft = 20;
        panelStyle.ContentMarginRight = 20;
        panelStyle.ContentMarginTop = 20;
        panelStyle.ContentMarginBottom = 20;
        containerPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(containerPanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        containerPanel.AddChild(vbox);

        var header = new Label();
        header.Text = headerText;
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeColorOverride("font_color", HeaderColor);
        header.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(header);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(scrollContainer);

        var gridContainer = new GridContainer();
        gridContainer.Columns = 6;
        gridContainer.AddThemeConstantOverride("h_separation", 12);
        gridContainer.AddThemeConstantOverride("v_separation", 12);
        gridContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scrollContainer.AddChild(gridContainer);

        if (_player.Relics.Count == 0)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = "No relics";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.AddThemeColorOverride("font_color", DimColor);
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            vbox.AddChild(emptyLabel);
            return;
        }

        for (int i = 0; i < _player.Relics.Count; i++)
        {
            var relic = _player.Relics[i];
            bool alreadyOffered = !_viewOnly && _currentOffer != null && _currentOffer.RelicIndices.Contains(i);
            bool blockedByHook = TradeConfig.BlockObtainHookRelics && TradeConfig.HasObtainHook(relic);
            bool disabled = alreadyOffered || blockedByHook;

            // Create relic icon
            Control? iconNode = null;
            try
            {
                var nRelic = NRelic.Create(relic, NRelic.IconSize.Small);
                if (nRelic != null)
                {
                    iconNode = nRelic;
                }
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Failed to create relic display: {e.Message}");
            }

            // Localized name
            string relicName;
            try { relicName = relic.Title.GetFormattedText(); }
            catch { relicName = relic.Id.Entry; }

            // Get hover tips for tooltip
            IEnumerable<IHoverTip>? tips = null;
            try { tips = relic.HoverTips; }
            catch { /* ignore */ }

            var entry = CreateItemEntry(iconNode, relicName, disabled, i, new Vector2(100, 130), 11,
                blocked: blockedByHook, hoverTips: tips);
            gridContainer.AddChild(entry);
        }
    }

    private void AddCancelButton()
    {
        var cancelButton = new Button();
        cancelButton.Text = _viewOnly ? "Close (Right-click)" : "Cancel (Right-click)";
        cancelButton.CustomMinimumSize = new Vector2(200, 45);
        cancelButton.AnchorLeft = 0.5f;
        cancelButton.AnchorTop = 1.0f;
        cancelButton.AnchorRight = 0.5f;
        cancelButton.AnchorBottom = 1.0f;
        cancelButton.OffsetLeft = -100;
        cancelButton.OffsetTop = -60;
        cancelButton.OffsetRight = 100;
        cancelButton.OffsetBottom = -15;
        cancelButton.GrowHorizontal = GrowDirection.Both;
        cancelButton.AddThemeColorOverride("font_color", CancelColor);
        cancelButton.AddThemeFontSizeOverride("font_size", 18);
        cancelButton.Pressed += () => Cancelled?.Invoke();
        AddChild(cancelButton);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouseEvent
            && mouseEvent.ButtonIndex == MouseButton.Right
            && mouseEvent.IsPressed())
        {
            Cancelled?.Invoke();
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventKey keyEvent
            && keyEvent.IsPressed()
            && keyEvent.Keycode == Key.Escape)
        {
            Cancelled?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        // Clean up tooltip when picker is removed
        HideTooltip();
    }
}
