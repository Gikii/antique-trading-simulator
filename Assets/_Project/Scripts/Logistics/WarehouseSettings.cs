using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// Tunable numbers for the player's warehouse: capacity and security per upgrade
    /// level, upgrade prices and daily upkeep. Index 0 of every array is the starting
    /// level. If none is assigned to PlayerTrader, an in-memory default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "WarehouseSettings", menuName = "AntiqueTradingSimulator/Logistics/Warehouse Settings")]
    public class WarehouseSettings : ScriptableObject
    {
        [Header("Capacity (1 antique = 1 slot)")]
        [Tooltip("Number of slots at each capacity level. Index 0 = starting warehouse.")]
        public int[] CapacityPerLevel = { 8, 12, 18, 26, 36 };

        [Tooltip("Price of upgrading TO each level (moving and shelving — the larger premises cost rent, " +
                 "see UpkeepPerCapacityLevel). Index 0 is unused (starting level).")]
        public float[] CapacityUpgradeCost = { 0f, 5f, 8f, 12f, 15f };

        [Header("Security")]
        [Tooltip("Price of upgrading TO each security level. Index 0 = no extra security. " +
                 "Security currently only raises upkeep; theft/damage risk will read it later.")]
        public float[] SecurityUpgradeCost = { 0f, 10f, 30f, 25f };

        [Tooltip("Weekly upkeep at each security level (£). Index 0 = no extra security. " +
                 "1: fire insurance on the stock, 2: a strong room, 3: plus a night watchman (24s).")]
        public float[] SecurityWeeklyUpkeep = { 0f, 0.05f, 0.2f, 1.4f };

        [Header("Weekly upkeep (£, 1884 prices: 0.05 = 1s) — charged once per period")]
        [FormerlySerializedAs("BaseDailyUpkeep")]
        [Min(0f)] public float BaseWeeklyUpkeep = 0.15f;      // 3s
        [Tooltip("Extra rent per capacity level: larger premises (three rooms in Soho let for 14–20s a week).")]
        [Min(0f)] public float UpkeepPerCapacityLevel = 0.35f; // 7s
        [Min(0f)] public float UpkeepPerUsedSlot = 0.05f;      // 1s

        public int MaxCapacityLevel => Mathf.Max(0, CapacityPerLevel.Length - 1);
        public int MaxSecurityLevel => Mathf.Max(0, SecurityUpgradeCost.Length - 1);

        /// <summary>Weekly upkeep of a security level (0 past the end of SecurityWeeklyUpkeep).</summary>
        public float SecurityUpkeep(int level) =>
            SecurityWeeklyUpkeep != null && level >= 0 && level < SecurityWeeklyUpkeep.Length
                ? SecurityWeeklyUpkeep[level]
                : 0f;
    }
}
