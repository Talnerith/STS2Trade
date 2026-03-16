using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;

namespace STS2Trade;

public static class TradeConfig
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

    private static string? _configPath;

    public static void Load()
    {
        try
        {
            // Config file sits next to the DLL
            var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dllDir == null) return;
            _configPath = Path.Combine(dllDir, "trade_config.json");

            if (!File.Exists(_configPath))
            {
                MainFile.Logger.Info($"[TradeConfig] No config file at {_configPath}, using defaults. Creating default config.");
                Save();
                return;
            }

            var json = File.ReadAllText(_configPath);
            var data = JsonSerializer.Deserialize<ConfigData>(json);
            if (data == null) return;

            MaxCardSlots = data.MaxCardSlots;
            MaxPotionSlots = data.MaxPotionSlots;
            MaxRelicSlots = data.MaxRelicSlots;
            UnlimitedTrades = data.UnlimitedTrades;
            BlockObtainHookRelics = data.BlockObtainHookRelics;
            BlockQuestCards = data.BlockQuestCards;

            MainFile.Logger.Info($"[TradeConfig] Loaded: UnlimitedTrades={UnlimitedTrades}, BlockObtainHookRelics={BlockObtainHookRelics}, BlockQuestCards={BlockQuestCards}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[TradeConfig] Failed to load config: {e.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            if (_configPath == null) return;

            var data = new ConfigData
            {
                MaxCardSlots = MaxCardSlots,
                MaxPotionSlots = MaxPotionSlots,
                MaxRelicSlots = MaxRelicSlots,
                UnlimitedTrades = UnlimitedTrades,
                BlockObtainHookRelics = BlockObtainHookRelics,
                BlockQuestCards = BlockQuestCards,
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(_configPath, json);
            MainFile.Logger.Info($"[TradeConfig] Saved config to {_configPath}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[TradeConfig] Failed to save config: {e.Message}");
        }
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

    private class ConfigData
    {
        [JsonPropertyName("maxCardSlots")]
        public int MaxCardSlots { get; set; } = 3;

        [JsonPropertyName("maxPotionSlots")]
        public int MaxPotionSlots { get; set; } = 3;

        [JsonPropertyName("maxRelicSlots")]
        public int MaxRelicSlots { get; set; } = 1;

        [JsonPropertyName("unlimitedTrades")]
        public bool UnlimitedTrades { get; set; } = false;

        [JsonPropertyName("blockObtainHookRelics")]
        public bool BlockObtainHookRelics { get; set; } = true;

        [JsonPropertyName("blockQuestCards")]
        public bool BlockQuestCards { get; set; } = true;
    }
}
