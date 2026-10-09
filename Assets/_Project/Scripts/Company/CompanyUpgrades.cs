using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using Newtonsoft.Json;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>What one upgrade level gives: an optional descriptive note and the effect values.</summary>
    public class UpgradeLevelEffects
    {
        public string Note = "";
        public List<UpgradeEffect> Effects = new List<UpgradeEffect>();
    }

    /// <summary>
    /// Snapshot of one upgrade for the UI: where it stands, what the next level gives and
    /// whether it can be bought right now. Levels are 0-based; DisplayLevel is 1-based.
    /// </summary>
    public class UpgradeInfo
    {
        public CompanyUpgradeType Type;
        public string Name;
        public string Description;
        public bool EffectActive;

        public int Level;
        public int LevelCount;
        public int DisplayLevel => Level + 1;
        public bool IsMaxed => Level >= LevelCount - 1;

        public UpgradeLevelEffects CurrentEffects;
        public UpgradeLevelEffects NextEffects;  // null when maxed

        public float CurrentUpkeep;
        public float NextUpkeep;      // = CurrentUpkeep when maxed
        public float NextCost;        // -1 when maxed

        public int RequiredReputation; // for the next level, 0 = none
        public bool MeetsReputation;
        public float Cash;             // player's cash when the info was taken
        public bool CanAfford;

        public bool CanUpgrade => !IsMaxed && MeetsReputation && CanAfford;

        /// <summary>Why the upgrade can't be bought right now, or empty if it can.</summary>
        public string BlockReason =>
            IsMaxed ? "Maximum level reached" :
            !MeetsReputation ? $"Requires {RequiredReputation} reputation" :
            !CanAfford ? "Not enough cash" : "";
    }

    /// <summary>
    /// Levels of the player's company upgrades, buying them, and what they do. Warehouse and
    /// Security live on the inventory's Warehouse (WarehouseSettings holds their numbers);
    /// every other upgrade keeps its level here.
    ///
    /// Effects are the numbers in CompanyUpgradeSettings (CompanyUpgradeLevel.Effects), read
    /// through GetStat — only for upgrades marked EffectActive. The upgrade levels are the
    /// single source of truth: the player's information access, trading modifiers and contract
    /// limit are derived from them, so restoring the levels from a save restores the effects.
    /// </summary>
    public class CompanyUpgrades
    {
        private readonly CompanyUpgradeSettings _settings;
        private readonly TraderInventory _inventory;
        private readonly Dictionary<CompanyUpgradeType, int> _levels = new Dictionary<CompanyUpgradeType, int>();

        public static readonly CompanyUpgradeType[] AllTypes =
            (CompanyUpgradeType[])Enum.GetValues(typeof(CompanyUpgradeType));

        /// <summary>Contract limit used when Staff gives none (or is inactive).</summary>
        public const int UnlimitedContracts = -1;

        public event Action<CompanyUpgradeType> OnUpgraded;

        [JsonIgnore]
        public CompanyUpgradeSettings Settings => _settings;

        /// <param name="informationNetworkStartLevel">
        /// 0-based starting level of Information Network for a new game. A loaded save
        /// overrides it (RestoreState).
        /// </param>
        public CompanyUpgrades(CompanyUpgradeSettings settings, TraderInventory inventory, int informationNetworkStartLevel = 0)
        {
            _settings = settings;
            _inventory = inventory;

            foreach (var type in AllTypes)
                if (!IsWarehouseType(type))
                    _levels[type] = 0;

            _levels[CompanyUpgradeType.InformationNetwork] =
                Mathf.Clamp(informationNetworkStartLevel, 0, Mathf.Max(0, LevelCount(CompanyUpgradeType.InformationNetwork) - 1));

            ApplyTradeModifiers();
        }

        private static bool IsWarehouseType(CompanyUpgradeType type) =>
            type == CompanyUpgradeType.Warehouse || type == CompanyUpgradeType.Security;

        private Warehouse Warehouse => _inventory?.Warehouse;

        // ---------------------------------------------------------------- levels

        public int GetLevel(CompanyUpgradeType type)
        {
            switch (type)
            {
                case CompanyUpgradeType.Warehouse: return Warehouse != null ? Warehouse.CapacityLevel : 0;
                case CompanyUpgradeType.Security: return Warehouse != null ? Warehouse.SecurityLevel : 0;
                default: return _levels.TryGetValue(type, out int level) ? level : 0;
            }
        }

        public int LevelCount(CompanyUpgradeType type)
        {
            switch (type)
            {
                case CompanyUpgradeType.Warehouse: return Warehouse != null ? Warehouse.Settings.MaxCapacityLevel + 1 : 1;
                case CompanyUpgradeType.Security: return Warehouse != null ? Warehouse.Settings.MaxSecurityLevel + 1 : 1;
                default:
                    var definition = _settings.Get(type);
                    return definition != null ? Mathf.Max(1, definition.Levels.Count) : 1;
            }
        }

        /// <summary>Reputation needed to upgrade TO a level (0-based), 0 = none.</summary>
        public int RequiredReputation(CompanyUpgradeType type, int level) =>
            LevelData(_settings.Get(type), level)?.RequiredReputation ?? 0;

        // ---------------------------------------------------------------- effects

        /// <summary>
        /// Value of a stat at the upgrade's current level, or <paramref name="fallback"/> if the
        /// upgrade is not EffectActive or its level doesn't define the stat.
        /// </summary>
        public float GetStat(CompanyUpgradeType type, UpgradeStat stat, float fallback)
        {
            var definition = _settings.Get(type);
            if (definition == null || !definition.EffectActive) return fallback;

            var level = LevelData(definition, GetLevel(type));
            return level != null && level.TryGetEffect(stat, out float value) ? value : fallback;
        }

        /// <summary>Which news the player receives — from the Information Network level.</summary>
        public InfoAccessLevel AccessLevel
        {
            get
            {
                int value = Mathf.RoundToInt(GetStat(CompanyUpgradeType.InformationNetwork, UpgradeStat.InfoAccessLevel,
                    (float)InfoAccessLevel.LocalPress));
                return (InfoAccessLevel)Mathf.Clamp(value, (int)InfoAccessLevel.LocalPress, (int)InfoAccessLevel.InternationalNetwork);
            }
        }

        /// <summary>How many contracts the player can have accepted at once (Staff); UnlimitedContracts = no limit.</summary>
        public int MaxActiveContracts
        {
            get
            {
                float value = GetStat(CompanyUpgradeType.Staff, UpgradeStat.MaxActiveContracts, UnlimitedContracts);
                return value < 0f ? UnlimitedContracts : Mathf.RoundToInt(value);
            }
        }

        /// <summary>
        /// Pushes Logistics and Staff values into the player's TraderInventory.Modifiers, which the
        /// Market and TransportManager read. Called whenever the levels change.
        /// </summary>
        public void ApplyTradeModifiers()
        {
            var modifiers = _inventory?.Modifiers;
            if (modifiers == null) return;

            modifiers.ListingFeeRate = GetStat(CompanyUpgradeType.Staff, UpgradeStat.MarketFeeRate, Market.Market.ListingFeeRate);
            modifiers.TransportCostReduction = GetStat(CompanyUpgradeType.Logistics, UpgradeStat.TransportCostReduction, 0f);
            modifiers.TransportDaysReduction = Mathf.RoundToInt(GetStat(CompanyUpgradeType.Logistics, UpgradeStat.TransportDaysReduction, 0f));
        }

        // ---------------------------------------------------------------- info

        public UpgradeInfo GetInfo(CompanyUpgradeType type, int reputation)
        {
            var definition = _settings.Get(type);
            int level = GetLevel(type);
            int count = LevelCount(type);
            bool maxed = level >= count - 1;

            var info = new UpgradeInfo
            {
                Type = type,
                Name = definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : type.ToString(),
                Description = definition?.Description ?? "",
                EffectActive = definition != null && definition.EffectActive,
                Level = level,
                LevelCount = count,
                CurrentEffects = LevelEffects(type, definition, level),
                NextEffects = maxed ? null : LevelEffects(type, definition, level + 1),
                CurrentUpkeep = Upkeep(type, definition, level),
                NextCost = maxed ? -1f : Cost(type, definition, level + 1),
                RequiredReputation = maxed ? 0 : LevelData(definition, level + 1)?.RequiredReputation ?? 0,
            };

            info.NextUpkeep = maxed ? info.CurrentUpkeep : Upkeep(type, definition, level + 1);
            info.MeetsReputation = reputation >= info.RequiredReputation;
            info.Cash = _inventory != null ? _inventory.Cash : 0f;
            info.CanAfford = !maxed && _inventory != null && _inventory.Cash >= info.NextCost;
            return info;
        }

        private static CompanyUpgradeLevel LevelData(CompanyUpgradeDefinition definition, int level) =>
            definition != null && level >= 0 && level < definition.Levels.Count ? definition.Levels[level] : null;

        private UpgradeLevelEffects LevelEffects(CompanyUpgradeType type, CompanyUpgradeDefinition definition, int level)
        {
            var result = new UpgradeLevelEffects();

            if (type == CompanyUpgradeType.Warehouse && Warehouse != null)
            {
                var capacities = Warehouse.Settings.CapacityPerLevel;
                int capacity = capacities.Length > 0 ? capacities[Mathf.Clamp(level, 0, capacities.Length - 1)] : 0;
                result.Effects.Add(new UpgradeEffect(UpgradeStat.WarehouseCapacity, capacity));
                return result;
            }

            var data = LevelData(definition, level);
            if (data == null) return result;

            result.Note = data.Note ?? "";
            if (data.Effects != null)
                result.Effects.AddRange(data.Effects);
            return result;
        }

        private float Cost(CompanyUpgradeType type, CompanyUpgradeDefinition definition, int level)
        {
            if (IsWarehouseType(type))
            {
                if (Warehouse == null) return 0f;
                float[] costs = type == CompanyUpgradeType.Warehouse
                    ? Warehouse.Settings.CapacityUpgradeCost
                    : Warehouse.Settings.SecurityUpgradeCost;
                return costs != null && level >= 0 && level < costs.Length ? costs[level] : 0f;
            }

            return LevelData(definition, level)?.Cost ?? 0f;
        }

        /// <summary>
        /// Daily upkeep at a level. Warehouse: the whole warehouse bill except its security
        /// part (base + capacity level + used slots). Security: the security part only.
        /// </summary>
        private float Upkeep(CompanyUpgradeType type, CompanyUpgradeDefinition definition, int level)
        {
            if (IsWarehouseType(type))
            {
                if (Warehouse == null) return 0f;
                var ws = Warehouse.Settings;
                return type == CompanyUpgradeType.Warehouse
                    ? ws.BaseDailyUpkeep + level * ws.UpkeepPerCapacityLevel + _inventory.UsedSlots * ws.UpkeepPerUsedSlot
                    : level * ws.UpkeepPerSecurityLevel;
            }

            return LevelData(definition, level)?.DailyUpkeep ?? 0f;
        }

        // ---------------------------------------------------------------- buying

        /// <summary>
        /// Buys the next level if it isn't maxed, the reputation requirement is met and there's
        /// enough cash. The price goes to the ledger as an Upgrade expense.
        /// </summary>
        public bool TryUpgrade(CompanyUpgradeType type, int reputation)
        {
            var info = GetInfo(type, reputation);
            if (!info.CanUpgrade) return false;

            bool success;
            switch (type)
            {
                case CompanyUpgradeType.Warehouse:
                    success = _inventory.TryUpgradeWarehouseCapacity();
                    break;
                case CompanyUpgradeType.Security:
                    success = _inventory.TryUpgradeWarehouseSecurity();
                    break;
                default:
                    _inventory.RemoveCash(info.NextCost, LedgerCategory.Upgrade, $"Upgraded {info.Name} to level {info.DisplayLevel + 1}");
                    _levels[type] = info.Level + 1;
                    success = true;
                    break;
            }

            if (success)
            {
                ApplyTradeModifiers();
                OnUpgraded?.Invoke(type);
            }
            return success;
        }

        /// <summary>
        /// Daily upkeep of every upgrade except Warehouse and Security, whose upkeep the
        /// warehouse already charges (TraderInventory.ChargeWarehouseUpkeep).
        /// </summary>
        public IEnumerable<(CompanyUpgradeType type, string name, float upkeep)> NonWarehouseUpkeep()
        {
            foreach (var type in AllTypes)
            {
                if (IsWarehouseType(type)) continue;

                var definition = _settings.Get(type);
                float upkeep = Upkeep(type, definition, GetLevel(type));
                if (upkeep > 0f)
                    yield return (type, definition?.DisplayName ?? type.ToString(), upkeep);
            }
        }

        // ---------------------------------------------------------------- save / load
        public Dictionary<CompanyUpgradeType, int> CaptureState() =>
            new Dictionary<CompanyUpgradeType, int>(_levels);

        /// <summary>
        /// Restores the levels and re-derives everything that depends on them (trading
        /// modifiers; access level and contract limit are read live from the levels).
        /// </summary>
        public void RestoreState(Dictionary<CompanyUpgradeType, int> levels)
        {
            if (levels != null)
            {
                foreach (var pair in levels)
                {
                    if (IsWarehouseType(pair.Key)) continue;

                    int maxLevel = Mathf.Max(0, LevelCount(pair.Key) - 1);
                    int level = Mathf.Clamp(pair.Value, 0, maxLevel);

                    if (level != pair.Value)
                        Debug.LogWarning($"CompanyUpgrades: saved level {pair.Value} for {pair.Key} is outside what the settings asset defines — clamped to {level}.");

                    _levels[pair.Key] = level;
                }
            }

            ApplyTradeModifiers();
        }

    }
}
