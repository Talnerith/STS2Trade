using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
// NPaginator not used — game's paginator.tscn can't be cast to NPaginator in mod context

namespace STS2Trade.Patches;

/// <summary>
/// Injects trade mod settings (checkboxes and paginators) into the mod info panel
/// when the STS2Trade mod is selected in the Modding Screen.
/// Settings appear below the mod description, matching the game's
/// settings screen style.
/// </summary>
[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class ModSettingsPatch
{
    private const string SettingsContainerName = "STS2TradeSettings";

    [HarmonyPostfix]
    public static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        // Remove any previously injected settings panel
        var existing = __instance.GetNodeOrNull(SettingsContainerName);
        if (existing != null)
            existing.QueueFree();

        // Only inject for our mod
        if (mod.manifest?.id != "STS2Trade") return;

        try
        {
            InjectSettings(__instance);
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"[ModSettingsPatch] Failed to inject settings: {e}");
        }
    }

    private static void InjectSettings(NModInfoContainer container)
    {
        var descriptionNode = container.GetNodeOrNull<RichTextLabel>("ModDescription");
        if (descriptionNode == null)
        {
            MainFile.Logger.Error("[ModSettingsPatch] ModDescription node not found");
            return;
        }

        // Save the original description text and position
        var descText = descriptionNode.Text;
        var descLeft = descriptionNode.OffsetLeft;
        var descTop = descriptionNode.OffsetTop;
        var descRight = descriptionNode.OffsetRight;
        var descBottom = container.OffsetBottom - container.OffsetTop - 20;

        // Hide the original description - we'll recreate it inside a scroll container
        descriptionNode.Visible = false;

        // Create a ScrollContainer that occupies the description area
        var scrollContainer = new ScrollContainer();
        scrollContainer.Name = SettingsContainerName;
        scrollContainer.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        scrollContainer.OffsetLeft = descLeft;
        scrollContainer.OffsetTop = descTop;
        scrollContainer.OffsetRight = descRight;
        scrollContainer.OffsetBottom = descBottom;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        // VBox holds description text + settings rows
        var contentBox = new VBoxContainer();
        contentBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        contentBox.AddThemeConstantOverride("separation", 8);

        // Recreate description as a Label (auto-sizing height)
        // Strip custom BBCode tags like [gold][/gold] that RichTextLabel doesn't understand
        var cleanedText = System.Text.RegularExpressions.Regex.Replace(
            descText, @"\[/?\w+\]", "");
        var descLabel = new RichTextLabel();
        descLabel.BbcodeEnabled = false;
        descLabel.Text = cleanedText;
        descLabel.FitContent = true;
        descLabel.ScrollActive = false;
        descLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        descLabel.AddThemeColorOverride("default_color", new Color("E8DCBE"));
        descLabel.AddThemeFontSizeOverride("normal_font_size", 20);
        descLabel.AddThemeFontSizeOverride("bold_font_size", 20);
        contentBox.AddChild(descLabel);

        // Divider before settings
        contentBox.AddChild(CreateDivider());

        // Settings header
        var headerLabel = new Label();
        headerLabel.Text = "Mod Settings";
        headerLabel.HorizontalAlignment = HorizontalAlignment.Left;
        headerLabel.AddThemeColorOverride("font_color", new Color("EFC851"));
        headerLabel.AddThemeFontSizeOverride("font_size", 22);
        contentBox.AddChild(headerLabel);

        // --- Checkbox settings ---

        // Unlimited Trades
        contentBox.AddChild(CreateSettingsRow(
            "Unlimited Trades",
            "Allow multiple trades per rest site",
            TradeConfig.UnlimitedTrades,
            (toggled) =>
            {
                TradeConfig.UnlimitedTrades = toggled;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] UnlimitedTrades set to {toggled}");
            }));

        contentBox.AddChild(CreateDivider());

        // Block Obtain Hook Relics
        contentBox.AddChild(CreateSettingsRow(
            "Block Obtain-Effect Relics",
            "Prevent trading relics that trigger effects when obtained",
            TradeConfig.BlockObtainHookRelics,
            (toggled) =>
            {
                TradeConfig.BlockObtainHookRelics = toggled;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] BlockObtainHookRelics set to {toggled}");
            }));

        contentBox.AddChild(CreateDivider());

        // Block Quest Cards
        contentBox.AddChild(CreateSettingsRow(
            "Block Quest Cards",
            "Prevent trading Quest-type cards",
            TradeConfig.BlockQuestCards,
            (toggled) =>
            {
                TradeConfig.BlockQuestCards = toggled;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] BlockQuestCards set to {toggled}");
            }));

        contentBox.AddChild(CreateDivider());

        // --- Paginator settings ---

        // Max Card Slots (1-5, default 3)
        contentBox.AddChild(CreatePaginatorRow(
            "Card Slots",
            1, 5, TradeConfig.MaxCardSlots,
            (value) =>
            {
                TradeConfig.MaxCardSlots = value;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] MaxCardSlots set to {value}");
            }));

        contentBox.AddChild(CreateDivider());

        // Max Potion Slots (1-3, default 3)
        contentBox.AddChild(CreatePaginatorRow(
            "Potion Slots",
            1, 3, TradeConfig.MaxPotionSlots,
            (value) =>
            {
                TradeConfig.MaxPotionSlots = value;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] MaxPotionSlots set to {value}");
            }));

        contentBox.AddChild(CreateDivider());

        // Max Relic Slots (1-3, default 1)
        contentBox.AddChild(CreatePaginatorRow(
            "Relic Slots",
            1, 3, TradeConfig.MaxRelicSlots,
            (value) =>
            {
                TradeConfig.MaxRelicSlots = value;
                TradeConfig.Save();
                MainFile.Logger.Info($"[ModSettings] MaxRelicSlots set to {value}");
            }));

        scrollContainer.AddChild(contentBox);
        container.AddChild(scrollContainer);
    }

    private static ColorRect CreateDivider()
    {
        var divider = new ColorRect();
        divider.CustomMinimumSize = new Vector2(0, 2);
        divider.Color = new Color(0.91f, 0.86f, 0.75f, 0.25f);
        divider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return divider;
    }

    /// <summary>
    /// Creates a settings row with label on the left and checkbox on the right.
    /// </summary>
    private static MarginContainer CreateSettingsRow(string label, string description, bool initialValue, System.Action<bool> onToggled)
    {
        var margin = new MarginContainer();
        margin.CustomMinimumSize = new Vector2(0, 56);
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 12);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;

        // Left side: label + description
        var textColumn = new VBoxContainer();
        textColumn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        textColumn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        textColumn.AddThemeConstantOverride("separation", 2);

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.AddThemeColorOverride("font_color", new Color("E8DCBE"));
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        textColumn.AddChild(nameLabel);

        var descLabel = new Label();
        descLabel.Text = description;
        descLabel.AddThemeColorOverride("font_color", new Color("E8DCBE80"));
        descLabel.AddThemeFontSizeOverride("font_size", 14);
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        hbox.AddChild(textColumn);

        // Right side: game's tickbox visuals
        var tickboxWrapper = CreateGameTickbox(initialValue, onToggled);
        hbox.AddChild(tickboxWrapper);

        margin.AddChild(hbox);
        return margin;
    }

    /// <summary>
    /// Creates a settings row with label on the left and a paginator (left/right arrows
    /// with value display) on the right, using the game's paginator scene.
    /// </summary>
    private static MarginContainer CreatePaginatorRow(string label, int min, int max, int currentValue, System.Action<int> onValueChanged)
    {
        var margin = new MarginContainer();
        margin.CustomMinimumSize = new Vector2(0, 56);
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 12);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;

        // Left side: label
        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.AddThemeColorOverride("font_color", new Color("E8DCBE"));
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(nameLabel);

        // Right side: custom paginator (game's paginator.tscn can't be cast to NPaginator in mod context)
        var paginatorBox = new HBoxContainer();
        paginatorBox.AddThemeConstantOverride("separation", 8);
        paginatorBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        int currentIdx = System.Math.Clamp(currentValue - min, 0, max - min);

        var valueLabel = new Label();
        valueLabel.Text = (min + currentIdx).ToString();
        valueLabel.CustomMinimumSize = new Vector2(30, 0);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
        valueLabel.AddThemeColorOverride("font_color", new Color("E8DCBE"));
        valueLabel.AddThemeFontSizeOverride("font_size", 20);

        var leftBtn = new Button();
        leftBtn.Text = "<";
        leftBtn.CustomMinimumSize = new Vector2(36, 36);
        leftBtn.AddThemeColorOverride("font_color", new Color("EFC851"));
        leftBtn.AddThemeFontSizeOverride("font_size", 20);
        leftBtn.Flat = true;

        var rightBtn = new Button();
        rightBtn.Text = ">";
        rightBtn.CustomMinimumSize = new Vector2(36, 36);
        rightBtn.AddThemeColorOverride("font_color", new Color("EFC851"));
        rightBtn.AddThemeFontSizeOverride("font_size", 20);
        rightBtn.Flat = true;

        leftBtn.Pressed += () =>
        {
            currentIdx--;
            if (currentIdx < 0) currentIdx = max - min;
            int value = min + currentIdx;
            valueLabel.Text = value.ToString();
            onValueChanged(value);
        };

        rightBtn.Pressed += () =>
        {
            currentIdx++;
            if (currentIdx > max - min) currentIdx = 0;
            int value = min + currentIdx;
            valueLabel.Text = value.ToString();
            onValueChanged(value);
        };

        paginatorBox.AddChild(leftBtn);
        paginatorBox.AddChild(valueLabel);
        paginatorBox.AddChild(rightBtn);
        hbox.AddChild(paginatorBox);

        margin.AddChild(hbox);
        return margin;
    }

    /// <summary>
    /// Instantiates the game's tickbox.tscn (same checkbox used in Settings)
    /// and wraps it in a clickable Button that toggles the ticked/unticked state.
    /// </summary>
    private static Control CreateGameTickbox(bool initialValue, System.Action<bool> onToggled)
    {
        // Load the game's tickbox scene: contains TickboxVisuals with Ticked/NotTicked TextureRects
        var tickboxScene = PreloadManager.Cache.GetScene("res://scenes/ui/tickbox.tscn");
        var tickboxVisuals = tickboxScene.Instantiate<Control>();

        // The scene root is "TickboxVisuals" containing "Ticked" and "NotTicked" children
        var tickedImage = tickboxVisuals.GetNode<Control>("Ticked");
        var notTickedImage = tickboxVisuals.GetNode<Control>("NotTicked");

        // Set initial state
        bool isTicked = initialValue;
        tickedImage.Visible = isTicked;
        notTickedImage.Visible = !isTicked;

        // Wrap in a transparent Button for click handling
        var button = new Button();
        button.CustomMinimumSize = new Vector2(64, 64);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.Flat = true;
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        button.AddChild(tickboxVisuals);

        button.Pressed += () =>
        {
            isTicked = !isTicked;
            tickedImage.Visible = isTicked;
            notTickedImage.Visible = !isTicked;
            onToggled(isTicked);
        };

        return button;
    }
}
