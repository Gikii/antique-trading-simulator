using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// Tunable numbers for the player's warehouse: capacity per upgrade level, upgrade
    /// prices and weekly upkeep. Index 0 of every array is the starting
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

        [Header("Weekly upkeep (£, 1884 prices: 0.05 = 1s) — charged once per period")]
        [FormerlySerializedAs("BaseDailyUpkeep")]
        [Min(0f)] public float BaseWeeklyUpkeep = 0.15f;      // 3s
        [Tooltip("Extra rent per capacity level: larger premises (three rooms in Soho let for 14–20s a week).")]
        [Min(0f)] public float UpkeepPerCapacityLevel = 0.35f; // 7s
        [Min(0f)] public float UpkeepPerUsedSlot = 0.05f;      // 1s

        public int MaxCapacityLevel => Mathf.Max(0, CapacityPerLevel.Length - 1);
    }
}
