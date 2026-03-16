#if DEBUG
using System;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace STS2Trade;

/// <summary>
/// Debug-only auto-test launcher. Handles command-line flags:
///
///   autohost          — open multiplayer test scene and click Host
///   autojoin [ID]     — open multiplayer test scene, set ID, click Join
///                       (ID defaults to 1000 if omitted)
///   autosize WxH      — resize window (e.g. 800x450)
///
/// Example batch script:
///   start game.exe --rendering-driver d3d12 -wpos 40 200 fastmp autosize 800x450 autohost
///   start game.exe --rendering-driver d3d12 -wpos 880 200 fastmp autosize 800x450 autojoin 1000
///   start game.exe --rendering-driver d3d12 -wpos 1720 200 fastmp autosize 800x450 autojoin 1001
///
/// Stripped from release builds via #if DEBUG.
/// </summary>
public partial class DebugAutoTest : Node
{
    private enum Phase { WaitForMainMenu, WaitForConsole, RunCommand, WaitForScene, Execute, Done }

    private Phase _phase = Phase.WaitForMainMenu;
    private double _delay;
    private bool _isHost;
    private ulong _joinId;
    private bool _resized;

    /// <summary>
    /// Call from MainFile.Initialize(). If autohost/autojoin flags are present,
    /// adds a DebugAutoTest node to the scene tree that drives the automation.
    /// </summary>
    public static void TryStart()
    {
        try
        {
            bool isHost = CommandLineHelper.HasArg("autohost");
            bool isJoin = CommandLineHelper.HasArg("autojoin");

            MainFile.Logger.Info($"[DebugAutoTest] TryStart: autohost={isHost}, autojoin={isJoin}");

            if (!isHost && !isJoin) return;

            ulong joinId = 1000;
            if (isJoin)
            {
                var val = CommandLineHelper.GetValue("autojoin");
                MainFile.Logger.Info($"[DebugAutoTest] autojoin value: '{val}'");
                if (val != null && ulong.TryParse(val, out var parsed))
                    joinId = parsed;
            }

            var node = new DebugAutoTest
            {
                _isHost = isHost,
                _joinId = joinId
            };

            // Deferred add — Initialize runs during game startup before tree is ready
            var tree = (SceneTree)Engine.GetMainLoop();
            MainFile.Logger.Info($"[DebugAutoTest] MainLoop={tree != null}, Root={tree?.Root != null}");
            tree.Root.CallDeferred(Node.MethodName.AddChild, node);

            MainFile.Logger.Info($"[DebugAutoTest] Queued: {(isHost ? "autohost" : $"autojoin {joinId}")}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[DebugAutoTest] TryStart FAILED: {e}");
        }
    }

    public override void _Process(double delta)
    {
        _delay -= delta;
        if (_delay > 0) return;

        // Apply window resize once (CommandLineHelper already parsed autosize → value)
        if (!_resized)
        {
            _resized = true;
            var sizeStr = CommandLineHelper.GetValue("autosize");
            if (sizeStr != null)
            {
                var parts = sizeStr.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                {
                    MainFile.Logger.Info($"[DebugAutoTest] Resizing window to {w}x{h}");
                    DisplayServer.WindowSetSize(new Vector2I(w, h));
                }
            }
        }

        switch (_phase)
        {
            case Phase.WaitForMainMenu:
                // Wait until the main menu is fully loaded — running console commands
                // during GameStartup/LaunchMainMenu causes ObjectDisposedException
                // because the scene change disposes the logo animation mid-tween.
                var mainMenu = FindInTree<NMainMenu>(GetTree().Root);
                if (mainMenu == null)
                {
                    _delay = 0.5;
                    return;
                }
                MainFile.Logger.Info("[DebugAutoTest] Main menu detected, proceeding");
                _phase = Phase.WaitForConsole;
                _delay = 0.5;
                break;

            case Phase.WaitForConsole:
                // Poll until the dev console singleton exists
                try
                {
                    _ = NDevConsole.Instance;
                }
                catch
                {
                    _delay = 0.5;
                    return;
                }
                _phase = Phase.RunCommand;
                _delay = 0.3;
                break;

            case Phase.RunCommand:
                MainFile.Logger.Info("[DebugAutoTest] Executing 'multiplayer test'");
                var result = NDevConsole.Instance._devConsole.ProcessCommand("multiplayer test");
                MainFile.Logger.Info($"[DebugAutoTest] Command result: success={result.success}, msg={result.msg}");
                _phase = Phase.WaitForScene;
                _delay = 2.0;
                break;

            case Phase.WaitForScene:
                var mpTest = FindInTree<NMultiplayerTest>(GetTree().Root);
                if (mpTest == null)
                {
                    MainFile.Logger.Info("[DebugAutoTest] Waiting for NMultiplayerTest node...");
                    _delay = 1.0;
                    return;
                }
                _phase = Phase.Execute;
                _delay = 0.5; // small delay to let the scene finish _Ready
                break;

            case Phase.Execute:
                var test = FindInTree<NMultiplayerTest>(GetTree().Root);
                if (test == null)
                {
                    _delay = 1.0;
                    return;
                }

                if (_isHost)
                {
                    MainFile.Logger.Info("[DebugAutoTest] Clicking Host");
                    test.HostButtonPressed();
                }
                else
                {
                    MainFile.Logger.Info($"[DebugAutoTest] Setting ID={_joinId} and clicking Join");
                    test._idField.Text = _joinId.ToString();
                    test.JoinButtonPressed();
                }

                _phase = Phase.Done;
                SetProcess(false);
                QueueFree();
                break;
        }
    }

    private static T? FindInTree<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindInTree<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
