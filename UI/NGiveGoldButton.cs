using System;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace STS2Trade.UI;

public partial class NGiveGoldButton : Control
{
    // Colors matching STS2 dark-panel-with-gold-accent style
    private static readonly Color GoldColor = new("EFC851");
    private static readonly Color TextColor = new("FFF6E2");
    private static readonly Color BgNormal = new(0.10f, 0.09f, 0.15f, 0.85f);
    private static readonly Color BgHover = new(0.16f, 0.14f, 0.22f, 0.90f);
    private static readonly Color BgPressed = new(0.08f, 0.07f, 0.12f, 0.95f);
    private static readonly Color BorderNormal = new Color("EFC851") * new Color(1, 1, 1, 0.4f);
    private static readonly Color BorderHover = new Color("EFC851");

    // Hold-to-repeat configuration
    private const float InitialDelay = 1.0f;
    private const float StartRate = 3.0f;
    private const float MaxRate = 15.0f;
    private const float AccelDuration = 3.0f;
    private const int TransferAmount = 50;

    private ulong _targetPlayerId;
    private Label? _label;
    private bool _isHovered;
    private bool _isPressed;
    private double _pressTime;
    private double _timeSinceLastTransfer;
    private bool _repeatStarted;

    public static NGiveGoldButton Create(ulong targetPlayerId)
    {
        var button = new NGiveGoldButton();
        button._targetPlayerId = targetPlayerId;
        return button;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(120, 52);
        Size = new Vector2(120, 52);
        MouseFilter = MouseFilterEnum.Stop;

        string playerName = "???";
        try
        {
            playerName = PlatformUtil.GetPlayerName(
                RunManager.Instance.NetService.Platform, _targetPlayerId);
        }
        catch { }

        _label = new Label();
        _label.Text = $"Give Gold to\n{playerName}";
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _label.AddThemeColorOverride("font_color", GoldColor);
        _label.AddThemeFontSizeOverride("font_size", 13);
        _label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_label);

        // Counteract parent modulate so all buttons look the same brightness
        // (character nodes can have different Modulate values for shading)
        Color accumulated = Colors.White;
        Node current = GetParent();
        while (current is CanvasItem ci)
        {
            accumulated *= ci.Modulate;
            current = current.GetParent();
        }
        if (accumulated != Colors.White)
        {
            Modulate = new Color(
                accumulated.R > 0.01f ? 1f / accumulated.R : 1f,
                accumulated.G > 0.01f ? 1f / accumulated.G : 1f,
                accumulated.B > 0.01f ? 1f / accumulated.B : 1f,
                1f
            );
        }

        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public override void _ExitTree()
    {
        _isPressed = false;
    }

    private void OnMouseEntered()
    {
        _isHovered = true;
        QueueRedraw();
    }

    private void OnMouseExited()
    {
        _isHovered = false;
        if (_isPressed)
        {
            _isPressed = false;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        var bg = _isPressed ? BgPressed : (_isHovered ? BgHover : BgNormal);
        var border = _isHovered || _isPressed ? BorderHover : BorderNormal;

        // Rounded background
        DrawRect(rect, bg);
        DrawRect(rect, border, false, 1.5f);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.IsPressed())
            {
                _isPressed = true;
                _pressTime = 0;
                _timeSinceLastTransfer = 0;
                _repeatStarted = false;

                // Immediate first transfer on press
                DoTransfer();

                QueueRedraw();
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _isPressed = false;
                QueueRedraw();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_isPressed) return;

        _pressTime += delta;

        // Wait for initial delay before starting repeats
        if (_pressTime < InitialDelay) return;

        if (!_repeatStarted)
        {
            _repeatStarted = true;
            _timeSinceLastTransfer = 0;
        }

        // Accelerating rate: lerp from StartRate to MaxRate over AccelDuration
        double repeatTime = _pressTime - InitialDelay;
        float t = (float)Math.Min(repeatTime / AccelDuration, 1.0);
        float currentRate = StartRate + (MaxRate - StartRate) * t;
        double interval = 1.0 / currentRate;

        _timeSinceLastTransfer += delta;
        while (_timeSinceLastTransfer >= interval)
        {
            _timeSinceLastTransfer -= interval;
            if (!DoTransfer())
                break;
        }
    }

    /// <summary>
    /// Attempts a gold transfer. Returns false if no gold left (stops repeating).
    /// </summary>
    private bool DoTransfer()
    {
        var sync = GoldGiftSynchronizer.Instance;
        if (sync == null) return false;

        int sent = sync.SendGold(_targetPlayerId, TransferAmount);
        if (sent <= 0)
        {
            // No gold left — auto-release
            _isPressed = false;
            QueueRedraw();
            return false;
        }
        return true;
    }
}
