using UnityEngine;

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

        [Tooltip("Price of upgrading TO each level. Index 0 is unused (starting level).")]
        public float[] CapacityUpgradeCost = { 0f, 600f, 1500f, 3500f, 7000f };

        [Header("Security")]
        [Tooltip("Price of upgrading TO each security level. Index 0 = no extra security. " +
                 "Security currently only raises upkeep; theft/damage risk will read it later.")]
        public float[] SecurityUpgradeCost = { 0f, 500f, 1200f, 3000f };

        [Header("Daily upkeep (€)")]
        [Min(0f)] public float BaseDailyUpkeep = 5f;
        [Min(0f)] public float UpkeepPerCapacityLevel = 3f;
        [Min(0f)] public float UpkeepPerSecurityLevel = 4f;
        [Min(0f)] public float UpkeepPerUsedSlot = 1f;

        public int MaxCapacityLevel => Mathf.Max(0, CapacityPerLevel.Length - 1);
        public int MaxSecurityLevel => Mathf.Max(0, SecurityUpgradeCost.Length - 1);
    }
}
