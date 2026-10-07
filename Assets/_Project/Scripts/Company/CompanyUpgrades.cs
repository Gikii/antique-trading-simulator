using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using Newtonsoft.Json;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
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

        public string CurrentEffect;
        public string NextEffect;     // empty when maxed

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
    /// Levels of the player's company upgrades and buying them. Warehouse and Security
    /// live on the inventory's Warehouse (WarehouseSettings holds their numbers); every
    /// other upgrade keeps its level here. Effects of the other upgrades are not read by
    /// any system yet (see CompanyUpgradeDefinition.EffectActive).
    /// </summary>
    public class CompanyUpgrades
    {
        private readonly CompanyUpgradeSettings _settings;
        private readonly TraderInventory _inventory;
        private readonly Dictionary<CompanyUpgradeType, int> _levels = new Dictionary<CompanyUpgradeType, int>();

        public static readonly CompanyUpgradeType[] AllTypes =
            (CompanyUpgradeType[])Enum.GetValues(typeof(CompanyUpgradeType));

        public event Action<CompanyUpgradeType> OnUpgraded;

        [JsonIgnore]
        public CompanyUpgradeSettings Settings => _settings;

        public CompanyUpgrades(CompanyUpgradeSettings settings, TraderInventory inventory, int informationNetworkStartLevel)
        {
            _settings = settings;
            _inventory = inventory;

            foreach (var type in AllTypes)
                if (!IsWarehouseType(type))
                    _levels[type] = 0;

            _levels[CompanyUpgradeType.InformationNetwork] =
                Mathf.Clamp(informationNetworkStartLevel, 0, Mathf.Max(0, LevelCount(CompanyUpgradeType.InformationNetwork) - 1));
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
                CurrentEffect = Effect(type, definition, level),
                NextEffect = maxed ? "" : Effect(type, definition, level + 1),
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

        private string Effect(CompanyUpgradeType type, CompanyUpgradeDefinition definition, int level)
        {
            if (type == CompanyUpgradeType.Warehouse && Warehouse != null)
            {
                var capacities = Warehouse.Settings.CapacityPerLevel;
                int capacity = capacities.Length > 0 ? capacities[Mathf.Clamp(level, 0, capacities.Length - 1)] : 0;
                return $"Capacity: {capacity} items";
            }

            return LevelData(definition, level)?.Effect ?? "";
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
                OnUpgraded?.Invoke(type);
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

        public void RestoreState(Dictionary<CompanyUpgradeType, int> levels)
        {
            if (levels == null) return;

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

    }
}
