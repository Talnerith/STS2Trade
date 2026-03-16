using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace STS2Trade.UI;

/// <summary>
/// Custom tooltip that renders inside the trade screen's own node tree,
/// avoiding z-order issues with the game's NHoverTipSet system.
/// Uses the game's own hover_tip.tscn scene for proper text formatting
/// (MegaRichTextLabel handles [blue], [gold], etc. BBCode tags).
/// Shows card previews for cards, styled tooltip panels for potions/relics.
/// </summary>
public partial class NTradeTooltip : Control
{
    private const string TipScenePath = "res://scenes/ui/hover_tip.tscn";
    private const string DebuffMatPath = "res://materials/ui/hover_tip_debuff.tres";
    private const float HoverTipWidth = 360f;
    private const float HoverTipSpacing = 5f;

    private static NTradeTooltip? _instance;

    private VFlowContainer? _tipContainer;
    private NCard? _cardPreview;

    public static NTradeTooltip GetOrCreate(Control parent)
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance) && _instance.IsInsideTree())
            return _instance;

        _instance = new NTradeTooltip();
        _instance.MouseFilter = MouseFilterEnum.Ignore;
        _instance.ZIndex = 50; // On top within the trade screen
        _instance.Visible = false;
        parent.AddChild(_instance);
        return _instance;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Follow mouse position
        var mousePos = GetGlobalMousePosition();
        PositionTooltip(mousePos);
    }

    public void ShowForCard(CardModel card, Control owner)
    {
        ClearContent();
        Visible = true;

        // Create a card preview using the game's NCard
        try
        {
            var nCard = NCard.Create(card);
            if (nCard != null)
            {
                _cardPreview = nCard;
                float scale = 0.7f;
                nCard.Scale = new Vector2(scale, scale);
                var cardSize = NCard.defaultSize * scale;
                nCard.Position = cardSize * 0.5f + new Vector2(8, 8);
                nCard.MouseFilter = MouseFilterEnum.Ignore;
                AddChild(nCard);
                DisableMouseRecursive(nCard);
                nCard.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);

                Size = cardSize + new Vector2(16, 16);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeTooltip: Failed to create card preview: {e.Message}");
        }

        PositionTooltip(GetGlobalMousePosition());
    }

    public void ShowForHoverTips(IEnumerable<IHoverTip> hoverTips, Control owner)
    {
        ClearContent();
        Visible = true;

        // Use the same VFlowContainer approach as the game's NHoverTipSet
        _tipContainer = new VFlowContainer();
        _tipContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_tipContainer);

        bool hasContent = false;
        foreach (var tip in hoverTips)
        {
            if (tip is CardHoverTip cardTip)
            {
                // For card hover tips, show as card preview instead
                _tipContainer.QueueFree();
                _tipContainer = null;
                ShowForCard(cardTip.Card, owner);
                return;
            }

            if (tip is HoverTip ht)
            {
                AddHoverTipEntry(ht);
                hasContent = true;
            }
        }

        if (!hasContent)
        {
            _tipContainer.QueueFree();
            _tipContainer = null;
            Visible = false;
            return;
        }

        // Wait one frame for the scene instances to run _Ready and calculate sizes
        var container = _tipContainer;
        var timer = GetTree().CreateTimer(0);
        timer.Timeout += () =>
        {
            if (container != null && GodotObject.IsInstanceValid(container))
                Size = container.Size;
        };

        PositionTooltip(GetGlobalMousePosition());
    }

    /// <summary>
    /// Instantiates the game's hover_tip.tscn scene and populates it, matching
    /// exactly what NHoverTipSet.Init does. This ensures proper MegaRichTextLabel
    /// rendering with [blue], [gold], [buff], etc. BBCode formatting.
    /// </summary>
    private void AddHoverTipEntry(HoverTip tip)
    {
        try
        {
            Control tipControl = PreloadManager.Cache.GetScene(TipScenePath)
                .Instantiate<Control>(PackedScene.GenEditState.Disabled);

            _tipContainer!.AddChild(tipControl);

            // Title
            var titleNode = tipControl.GetNode<MegaLabel>("%Title");
            if (tip.Title == null)
            {
                titleNode.Visible = false;
            }
            else
            {
                titleNode.SetTextAutoSize(tip.Title);
            }

            // Description
            var descNode = tipControl.GetNode<MegaRichTextLabel>("%Description");
            descNode.Text = tip.Description;
            descNode.AutowrapMode = tip.ShouldOverrideTextOverflow
                ? TextServer.AutowrapMode.Off
                : TextServer.AutowrapMode.WordSmart;

            // Icon
            tipControl.GetNode<TextureRect>("%Icon").Texture = tip.Icon;

            // Debuff styling
            if (tip.IsDebuff)
            {
                tipControl.GetNode<CanvasItem>("%Bg").Material =
                    PreloadManager.Cache.GetMaterial(DebuffMatPath);
            }

            tipControl.ResetSize();

            // Update container size to accommodate the new entry
            if (_tipContainer.Size.Y + tipControl.Size.Y + HoverTipSpacing
                < NGame.Instance.GetViewportRect().Size.Y - 50f)
            {
                _tipContainer.Size = new Vector2(
                    HoverTipWidth,
                    _tipContainer.Size.Y + tipControl.Size.Y + HoverTipSpacing);
            }
            else
            {
                _tipContainer.Alignment = FlowContainer.AlignmentMode.Center;
            }

            DisableMouseRecursive(tipControl);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"NTradeTooltip: Failed to create hover tip entry: {e.Message}");
        }
    }

    public void Hide()
    {
        ClearContent();
        Visible = false;
    }

    private void ClearContent()
    {
        _cardPreview = null;
        _tipContainer = null;
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private void PositionTooltip(Vector2 mousePos)
    {
        var viewport = GetViewportRect().Size;
        // Position well to the right of cursor to avoid overlap
        float x = mousePos.X + 80;
        float y = mousePos.Y - 10;

        // Keep on screen — flip to left side if needed
        if (x + Size.X > viewport.X)
            x = mousePos.X - Size.X - 40;
        if (y + Size.Y > viewport.Y)
            y = viewport.Y - Size.Y - 5;
        if (y < 5)
            y = 5;

        GlobalPosition = new Vector2(x, y);
    }

    private static void DisableMouseRecursive(Node node)
    {
        if (node is Control control)
            control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren())
            DisableMouseRecursive(child);
    }
}
