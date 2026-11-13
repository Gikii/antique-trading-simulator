using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// Tunable numbers for the transport system: how much shipping costs per zone,
    /// how long each service takes and how risky it is. Author one asset in
    /// Data/ and assign it to TransportManager — if none is assigned, the manager
    /// falls back to an in-memory instance with the defaults below.
    /// </summary>
    [CreateAssetMenu(fileName = "TransportSettings", menuName = "AntiqueTradingSimulator/Logistics/Transport Settings")]
    public class TransportSettings : ScriptableObject
    {
        [Serializable]
        public struct ZoneCost
        {
            [Tooltip("Base shipping cost as a fraction of the item's sale price.")]
            [Range(0f, 0.5f)] public float RateOfItemPrice;

            [Tooltip("Base shipping cost never goes below this amount (€).")]
            [Min(0f)] public float MinimumFee;
        }

        [Serializable]
        public struct OptionSettings
        {
            [FormerlySerializedAs("LocalDays")]
            [Min(1)] public int LocalWeeks;
            [FormerlySerializedAs("DomesticDays")]
            [Min(1)] public int DomesticWeeks;
            [FormerlySerializedAs("InternationalDays")]
            [Min(1)] public int InternationalWeeks;

            [Tooltip("Multiplies the zone's base cost.")]
            [Min(0f)] public float CostMultiplier;

            [Tooltip("Chance that the item arrives damaged (rolled once, on delivery).")]
            [Range(0f, 1f)] public float DamageChance;
        }

        [Header("Base cost per zone")]
        public ZoneCost Local = new ZoneCost { RateOfItemPrice = 0.02f, MinimumFee = 10f };
        public ZoneCost Domestic = new ZoneCost { RateOfItemPrice = 0.05f, MinimumFee = 25f };
        public ZoneCost International = new ZoneCost { RateOfItemPrice = 0.10f, MinimumFee = 60f };

        [Header("Transport services")]
        public OptionSettings Economy = new OptionSettings
        {
            LocalWeeks = 2, DomesticWeeks = 4, InternationalWeeks = 8,
            CostMultiplier = 0.6f, DamageChance = 0.08f
        };

        public OptionSettings Standard = new OptionSettings
        {
            LocalWeeks = 1, DomesticWeeks = 3, InternationalWeeks = 5,
            CostMultiplier = 1f, DamageChance = 0.03f
        };

        public OptionSettings Express = new OptionSettings
        {
            LocalWeeks = 1, DomesticWeeks = 1, InternationalWeeks = 2,
            CostMultiplier = 2.5f, DamageChance = 0f
        };

        [Header("Damage in transit")]
        [Tooltip("Condition lost when an item arrives damaged — rolled uniformly between Min and Max.")]
        [Range(0f, 1f)] public float MinConditionLoss = 0.05f;
        [Range(0f, 1f)] public float MaxConditionLoss = 0.2f;

        public ZoneCost GetZoneCost(ShippingZone zone) => zone switch
        {
            ShippingZone.Local => Local,
            ShippingZone.Domestic => Domestic,
            ShippingZone.International => International,
            _ => Domestic
        };

        public OptionSettings GetOption(TransportOption option) => option switch
        {
            TransportOption.Economy => Economy,
            TransportOption.Standard => Standard,
            TransportOption.Express => Express,
            _ => Standard
        };

        /// <summary>Delivery time in days, before any event-driven delays.</summary>
        public int GetBaseDurationWeeks(ShippingZone zone, TransportOption option)
        {
            var settings = GetOption(option);
            int weeks = zone switch
            {
                ShippingZone.Local => settings.LocalWeeks,
                ShippingZone.Domestic => settings.DomesticWeeks,
                ShippingZone.International => settings.InternationalWeeks,
                _ => settings.DomesticWeeks
            };
            return Mathf.Max(1, weeks);
        }

        public float GetCost(float itemPrice, ShippingZone zone, TransportOption option)
        {
            var zoneCost = GetZoneCost(zone);
            float baseCost = Mathf.Max(itemPrice * zoneCost.RateOfItemPrice, zoneCost.MinimumFee);
            return baseCost * GetOption(option).CostMultiplier;
        }

        public float GetDamageChance(TransportOption option) => GetOption(option).DamageChance;

        public float RollConditionLoss()
        {
            float min = Mathf.Min(MinConditionLoss, MaxConditionLoss);
            float max = Mathf.Max(MinConditionLoss, MaxConditionLoss);
            return UnityEngine.Random.Range(min, max);
        }
    }
}
