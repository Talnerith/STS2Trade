using System.Reflection;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace STS2Trade;

public class TradeConfig : ModConfig
{
    public static int MaxCardSlots { get; set; } = 3;
    public static int MaxPotionSlots { get; set; } = 3;
    public static int MaxRelicSlots { get; set; } = 1;

    /// <summary>
    /// When true, players can trade multiple times per rest site.
    /// Default: false (one trade per player per rest site).
    /// </summary>
    public static bool UnlimitedTrades { get; set; } = false;

    /// <summary>
    /// When true, relics with AfterObtained hooks (Whetstone, War Paint, etc.)
    /// are blocked from being traded to prevent unexpected side effects.
    /// Default: true.
    /// </summary>
    public static bool BlockObtainHookRelics { get; set; } = true;

    /// <summary>
    /// When true, Quest-type cards cannot be traded.
    /// Default: true.
    /// </summary>
    public static bool BlockQuestCards { get; set; } = true;

    public TradeConfig() : base("STS2Trade.cfg") { }

    public override void SetupConfigUI(Control optionContainer)
    {
        var options = new VBoxContainer();
        options.Size = optionContainer.Size;
        options.AddThemeConstantOverride("separation", 8);
        optionContainer.AddChild(options);

        foreach (var property in ConfigProperties)
        {
            if (property.PropertyType == typeof(bool))
            {
                MakeToggleOption(options, property);
            }
            else if (property.PropertyType == typeof(int))
            {
                MakePaginatorOption(options, property);
            }
        }
    }

    private void MakePaginatorOption(Control parent, PropertyInfo property)
    {
        var (min, max, step) = property.Name switch
        {
            nameof(MaxCardSlots) => (1, 5, 1),
            nameof(MaxPotionSlots) => (1, 3, 1),
            nameof(MaxRelicSlots) => (1, 3, 1),
            _ => (1, 10, 1)
        };

        var container = MakeOptionContainer(parent, "Paginator_" + property.Name, property.Name);

        var paginatorBox = new HBoxContainer();
        paginatorBox.AddThemeConstantOverride("separation", 8);
        paginatorBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        paginatorBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

        int currentVal = Math.Clamp((int)(property.GetValue(null) ?? min), min, max);

        var valueLabel = new Label();
        valueLabel.Text = currentVal.ToString();
        valueLabel.CustomMinimumSize = new Vector2(40, 0);
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
            currentVal -= step;
            if (currentVal < min) currentVal = max;
            valueLabel.Text = currentVal.ToString();
            property.SetValue(null, currentVal);
            Changed();
        };

        rightBtn.Pressed += () =>
        {
            currentVal += step;
            if (currentVal > max) currentVal = min;
            valueLabel.Text = currentVal.ToString();
            property.SetValue(null, currentVal);
            Changed();
        };

        paginatorBox.AddChild(leftBtn);
        paginatorBox.AddChild(valueLabel);
        paginatorBox.AddChild(rightBtn);
        container.AddChild(paginatorBox);
    }

    /// <summary>
    /// Returns true if the given relic has an AfterObtained hook that does
    /// something beyond the base no-op (i.e., the method is overridden).
    /// </summary>
    public static bool HasObtainHook(RelicModel relic)
    {
        var method = relic.GetType().GetMethod("AfterObtained",
            BindingFlags.Instance | BindingFlags.Public);
        return method != null && method.DeclaringType != typeof(RelicModel);
    }
}
