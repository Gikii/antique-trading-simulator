using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// The physical storage behind a TraderInventory: how many antiques it can hold,
    /// how well it is secured and what it costs to run. Holds only levels — the
    /// numbers behind them live in WarehouseSettings, and cash is handled by the
    /// owning TraderInventory (TryUpgrade…, ChargeWarehouseUpkeep).
    ///
    /// Every owned antique takes one slot, including items still in transit (space
    /// is reserved at purchase) and items listed on the market (they are still stored
    /// until a buyer takes them).
    /// </summary>
    [Serializable]
    public class Warehouse
    {
        [SerializeField] private int capacityLevel;
        [SerializeField] private int securityLevel;

        [NonSerialized] private WarehouseSettings _settings;

        public int CapacityLevel => capacityLevel;
        public int SecurityLevel => securityLevel;
        public WarehouseSettings Settings => _settings;

        public Warehouse(WarehouseSettings settings, int startingCapacityLevel = 0, int startingSecurityLevel = 0)
        {
            _settings = settings;
            capacityLevel = Mathf.Clamp(startingCapacityLevel, 0, settings.MaxCapacityLevel);
            securityLevel = Mathf.Clamp(startingSecurityLevel, 0, settings.MaxSecurityLevel);
        }

        public int Capacity => _settings.CapacityPerLevel.Length > 0
            ? _settings.CapacityPerLevel[Mathf.Clamp(capacityLevel, 0, _settings.CapacityPerLevel.Length - 1)]
            : 0;

        // ---------------------------------------------------------------- capacity upgrades

        public bool CanUpgradeCapacity => capacityLevel < _settings.MaxCapacityLevel;

        /// <summary>Price of the next capacity level, or -1 when already at max.</summary>
        public float NextCapacityUpgradeCost =>
            CanUpgradeCapacity ? CostAt(_settings.CapacityUpgradeCost, capacityLevel + 1) : -1f;

        public int NextCapacity =>
            CanUpgradeCapacity ? _settings.CapacityPerLevel[capacityLevel + 1] : Capacity;

        // ---------------------------------------------------------------- security upgrades

        public bool CanUpgradeSecurity => securityLevel < _settings.MaxSecurityLevel;

        /// <summary>Price of the next security level, or -1 when already at max.</summary>
        public float NextSecurityUpgradeCost =>
            CanUpgradeSecurity ? CostAt(_settings.SecurityUpgradeCost, securityLevel + 1) : -1f;

        // ---------------------------------------------------------------- upkeep

        public float CalculateDailyUpkeep(int usedSlots)
        {
            return _settings.BaseDailyUpkeep
                   + capacityLevel * _settings.UpkeepPerCapacityLevel
                   + securityLevel * _settings.UpkeepPerSecurityLevel
                   + Mathf.Max(0, usedSlots) * _settings.UpkeepPerUsedSlot;
        }

        // ---------------------------------------------------------------- level changes
        // Called by TraderInventory after it has taken the payment.

        internal bool RaiseCapacityLevel()
        {
            if (!CanUpgradeCapacity) return false;
            capacityLevel++;
            return true;
        }

        internal bool RaiseSecurityLevel()
        {
            if (!CanUpgradeSecurity) return false;
            securityLevel++;
            return true;
        }

        private static float CostAt(float[] costs, int level) =>
            costs != null && level >= 0 && level < costs.Length ? costs[level] : 0f;
    }
}
