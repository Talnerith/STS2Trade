using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace STS2Trade.UI;

public partial class NTradeSlot : Control
{
    public enum SlotType
    {
        Card,
        Potion,
        Relic
    }

    private static readonly Color EmptyColor = new(0.12f, 0.11f, 0.18f, 0.8f);
    private static readonly Color FilledColor = new(0.16f, 0.15f, 0.24f, 0.9f);
    private static readonly Color HoverColor = new(0.22f, 0.20f, 0.32f, 0.95f);
    private static readonly Color BorderNormal = new Color("EFC851") * new Color(1, 1, 1, 0.3f);
    private static readonly Color BorderHover = new Color("EFC851") * new Color(1, 1, 1, 0.7f);
    private static readonly Color BorderFilled = new Color("EFC851") * new Color(1, 1, 1, 0.5f);
    private static readonly Color ItemTextColor = new Color("FFF6E2"); // StsColors.cream
    private static readonly Color EmptyTextColor = new Color("FFF6E280"); // StsColors.halfTransparentCream

    private SlotType _slotType;
    private bool _isLocal;
    private int _slotIndex;
    private bool _hasItem;
    private string _itemName = "";
    private bool _isHovered;
    private IEnumerable<IHoverTip>? _hoverTips;
    private CardModel? _cardModel; // Stored separately for card preview rendering

    private Label _label;

    public event Action? SlotClicked;

    public static NTradeSlot Create(SlotType type, bool isLocal, int index)
    {
        var slot = new NTradeSlot();
        slot._slotType = type;
        slot._isLocal = isLocal;
        slot._slotIndex = index;
        return slot;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(145, 48);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Both local and partner slots respond to hover for tooltip display
        MouseFilter = MouseFilterEnum.Stop;

        _label = new Label();
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_label);

        UpdateDisplay();

        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    private void OnMouseEntered()
    {
        _isHovered = true;
        QueueRedraw();

        if (!_hasItem) return;

        try
        {
            // Find the trade screen parent to host the tooltip
            var tradeScreen = FindTradeScreenParent();
            if (tradeScreen == null) return;

            var tooltip = NTradeTooltip.GetOrCreate(tradeScreen);

            if (_cardModel != null)
            {
                tooltip.ShowForCard(_cardModel, this);
            }
            else if (_hoverTips != null)
            {
                tooltip.ShowForHoverTips(_hoverTips, this);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeSlot: Failed to show tooltip: {e.Message}");
        }
    }

    private void OnMouseExited()
    {
        _isHovered = false;
        QueueRedraw();

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

    public override void _ExitTree()
    {
        // Clean up tooltip if we're leaving the tree while hovered
        if (_isHovered)
        {
            try
            {
                var tradeScreen = FindTradeScreenParent();
                if (tradeScreen != null)
                {
                    var tooltip = NTradeTooltip.GetOrCreate(tradeScreen);
                    tooltip.Hide();
                }
            }
            catch { /* ignore */ }
        }
    }

    public override void _Draw()
    {
        Color bg;
        Color border;
        if (_isHovered && (_isLocal || _hasItem))
        {
            bg = HoverColor;
            border = BorderHover;
        }
        else if (_hasItem)
        {
            bg = FilledColor;
            border = BorderFilled;
        }
        else
        {
            bg = EmptyColor;
            border = BorderNormal;
        }

        // Rounded rect
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, bg);
        DrawRect(rect, border, false, 1.5f);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (!_isLocal) return;

        if (inputEvent is InputEventMouseButton mouseEvent
            && mouseEvent.ButtonIndex == MouseButton.Left
            && mouseEvent.IsPressed())
        {
            SlotClicked?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetItem(string name, IEnumerable<IHoverTip>? hoverTips = null, CardModel? cardModel = null)
    {
        _hasItem = true;
        _itemName = name;
        _hoverTips = hoverTips;
        _cardModel = cardModel;
        UpdateDisplay();
        QueueRedraw();
    }

    public void ClearItem()
    {
        _hasItem = false;
        _itemName = "";
        _hoverTips = null;
        _cardModel = null;
        UpdateDisplay();
        QueueRedraw();
    }

    private void UpdateDisplay()
    {
        if (_label == null) return;

        if (_hasItem)
        {
            _label.Text = _itemName;
            _label.AddThemeColorOverride("font_color", ItemTextColor);
        }
        else
        {
            string typeStr = _slotType switch
            {
                SlotType.Card => "+ Card",
                SlotType.Potion => "+ Potion",
                SlotType.Relic => "+ Relic",
                _ => "+ Item"
            };
            _label.Text = _isLocal ? typeStr : "(empty)";
            _label.AddThemeColorOverride("font_color", EmptyTextColor);
        }
    }
}
