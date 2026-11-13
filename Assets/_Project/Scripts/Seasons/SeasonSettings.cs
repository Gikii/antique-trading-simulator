using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Logistics;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Seasons
{
    /// <summary>
    /// What each season changes in the game: transport (weather delays, early arrivals, damage risk
    /// and carrier prices) and the market (demand per antique type, NPC activity, private collectors,
    /// new offers). Author one asset in Data/ and assign it to the TimeManager — if none is assigned,
    /// the defaults below are used.
    ///
    /// Transport follows the calendar season of the week (a clear rule for the player). The market
    /// changes gradually instead: the numbers of a season are its peak in mid-season, and in between
    /// the market blends from one season to the next (see GetMarketConditions), so there is no price
    /// jump on the first day of winter to exploit.
    ///
    /// Weather is a risk, not a fixed cost: a shipment keeps its quoted arrival week, but in the
    /// week it is due it may be held up one more week (delay chance), and in the week before it
    /// may arrive one week early (early chance). Each shipment gets at most one such shift; both
    /// chances use the season of the week the shipment is due.
    /// </summary>
    [CreateAssetMenu(fileName = "SeasonSettings", menuName = "AntiqueTradingSimulator/Season Settings")]
    public class SeasonSettings : ScriptableObject
    {
        [Serializable]
        public struct ZoneOdds
        {
            [Tooltip("Chance a shipment due this season is held up one extra week (Standard service).")]
            [Range(0f, 1f)] public float DelayChance;

            [Tooltip("Chance a shipment due this season arrives one week early (never sooner than a week after dispatch).")]
            [Range(0f, 1f)] public float EarlyChance;

            public ZoneOdds(float delayChance, float earlyChance)
            {
                DelayChance = delayChance;
                EarlyChance = earlyChance;
            }
        }

        [Serializable]
        public struct TypeDemand
        {
            public AntiqueType Type;

            [Tooltip("Demand for this antique type at the peak of the season, relative to normal (1.2 = +20%).")]
            [Range(0.5f, 1.5f)] public float Multiplier;

            public TypeDemand(AntiqueType type, float multiplier)
            {
                Type = type;
                Multiplier = multiplier;
            }
        }

        [Serializable]
        public class SeasonProfile
        {
            [Tooltip("One line on what the season means for trade — shown to the player.")]
            [TextArea] public string Description;

            [Header("Transport")]
            public ZoneOdds Local;
            public ZoneOdds Domestic;
            public ZoneOdds International;

            [Tooltip("Multiplies the transport damage chance for shipments delivered this season.")]
            [Min(0f)] public float DamageChanceMultiplier = 1f;

            [Tooltip("Multiplies the transport price of shipments dispatched this season.")]
            [Min(0f)] public float TransportCostMultiplier = 1f;

            [Header("Market (values at the peak of the season)")]
            [Tooltip("Multiplies how many actions NPC traders take per week.")]
            [Min(0f)] public float NpcActivity = 1f;

            [Tooltip("Multiplies the weekly chance that private collectors buy owner-set listings.")]
            [Min(0f)] public float CollectorChance = 1f;

            [Tooltip("Multiplies how many anonymous offers appear on the market each week.")]
            [Min(0f)] public float NewListings = 1f;

            [Tooltip("Demand changes per antique type. Types not listed keep their normal demand.")]
            public List<TypeDemand> Demand = new List<TypeDemand>();

            public float GetDemandMultiplier(AntiqueType type)
            {
                if (Demand == null) return 1f;
                foreach (var entry in Demand)
                    if (entry.Type == type)
                        return Mathf.Max(0f, entry.Multiplier);
                return 1f;
            }

            public ZoneOdds GetOdds(ShippingZone zone) => zone switch
            {
                ShippingZone.Local => Local,
                ShippingZone.Domestic => Domestic,
                ShippingZone.International => International,
                _ => Domestic
            };
        }

        [Header("Seasons")]
        public SeasonProfile Spring = new SeasonProfile
        {
            Description = "Society returns to town and estates come to market after the winter — furniture and paintings " +
                          "are sought after. Thaw and spring rains flood the country roads, holding up inland deliveries.",
            Local = new ZoneOdds(0.05f, 0f),
            Domestic = new ZoneOdds(0.20f, 0f),
            International = new ZoneOdds(0.10f, 0.05f),
            DamageChanceMultiplier = 1.2f,
            TransportCostMultiplier = 1f,
            NpcActivity = 1.1f,
            CollectorChance = 1.1f,
            NewListings = 1.15f,
            Demand = new List<TypeDemand>
            {
                new TypeDemand(AntiqueType.Furniture, 1.10f),
                new TypeDemand(AntiqueType.Painting, 1.10f)
            }
        };

        public SeasonProfile Summer = new SeasonProfile
        {
            Description = "The London Season: collectors are in town, exhibitions are open, and paintings and garden " +
                          "sculpture sell well. Dry roads and calm seas — shipments may even arrive early, but busy carriers charge more.",
            Local = new ZoneOdds(0f, 0f),
            Domestic = new ZoneOdds(0.05f, 0.10f),
            International = new ZoneOdds(0.05f, 0.20f),
            DamageChanceMultiplier = 1f,
            TransportCostMultiplier = 1.1f,
            NpcActivity = 1.2f,
            CollectorChance = 1.25f,
            NewListings = 1f,
            Demand = new List<TypeDemand>
            {
                new TypeDemand(AntiqueType.Painting, 1.15f),
                new TypeDemand(AntiqueType.Sculpture, 1.15f),
                new TypeDemand(AntiqueType.Porcelain, 0.95f)
            }
        };

        public SeasonProfile Autumn = new SeasonProfile
        {
            Description = "Society leaves for the country and the market quietens, though coin and clock collectors stay busy. " +
                          "Gales on the Channel and the North Sea, and railways busy with the harvest.",
            Local = new ZoneOdds(0.05f, 0f),
            Domestic = new ZoneOdds(0.15f, 0f),
            International = new ZoneOdds(0.30f, 0f),
            DamageChanceMultiplier = 1.25f,
            TransportCostMultiplier = 1f,
            NpcActivity = 0.85f,
            CollectorChance = 0.8f,
            NewListings = 0.9f,
            Demand = new List<TypeDemand>
            {
                new TypeDemand(AntiqueType.Coin, 1.10f),
                new TypeDemand(AntiqueType.Clock, 1.05f),
                new TypeDemand(AntiqueType.Painting, 0.95f)
            }
        };

        public SeasonProfile Winter = new SeasonProfile
        {
            Description = "Christmas buyers want porcelain and clocks, while few sellers bring anything to market. " +
                          "Snow, ice and frozen ports — expect delays, and fragile pieces break more easily.",
            Local = new ZoneOdds(0.15f, 0f),
            Domestic = new ZoneOdds(0.30f, 0f),
            International = new ZoneOdds(0.45f, 0f),
            DamageChanceMultiplier = 1.5f,
            TransportCostMultiplier = 1.15f,
            NpcActivity = 0.9f,
            CollectorChance = 1f,
            NewListings = 0.8f,
            Demand = new List<TypeDemand>
            {
                new TypeDemand(AntiqueType.Porcelain, 1.20f),
                new TypeDemand(AntiqueType.Clock, 1.15f),
                new TypeDemand(AntiqueType.Furniture, 0.95f),
                new TypeDemand(AntiqueType.Sculpture, 0.85f)
            }
        };

        [Serializable]
        public struct SeverityEffect
        {
            [Tooltip("Multiplies the season's delay chances.")]
            [Min(0f)] public float DelayMultiplier;

            [Tooltip("Multiplies the season's early-arrival chances.")]
            [Min(0f)] public float EarlyMultiplier;

            [Tooltip("Multiplies the transport damage chance.")]
            [Min(0f)] public float DamageMultiplier;

            public SeverityEffect(float delay, float early, float damage)
            {
                DelayMultiplier = delay;
                EarlyMultiplier = early;
                DamageMultiplier = damage;
            }

            public static SeverityEffect None => new SeverityEffect(1f, 1f, 1f);
        }

        [Header("Weather — how harsh each season turns out")]
        [Tooltip("Chance a season turns out mild (rolled a few weeks before it starts).")]
        [Range(0f, 1f)] public float MildChance = 0.25f;
        [Tooltip("Chance a season turns out harsh. The rest is an ordinary season.")]
        [Range(0f, 1f)] public float HarshChance = 0.25f;

        public SeverityEffect Mild = new SeverityEffect(0.5f, 1.5f, 0.8f);
        public SeverityEffect Harsh = new SeverityEffect(1.6f, 0.5f, 1.3f);

        [Header("Weather forecasts (news)")]
        [Tooltip("How many weeks before a season starts the forecasts come out.")]
        [Min(1)] public int ForecastLeadWeeks = 3;

        [Tooltip("The almanac — a rumour everyone reads. Chance it predicts the right weather.")]
        [Range(0f, 1f)] public float AlmanacAccuracy = 0.6f;

        [Tooltip("Shipping insurers' report — a leak for traders with an informant network, a week after the almanac. " +
                 "Chance it predicts the right weather.")]
        [Range(0f, 1f)] public float InsiderAccuracy = 0.9f;

        public SeverityEffect GetSeverityEffect(WeatherSeverity severity) => severity switch
        {
            WeatherSeverity.Mild => Mild,
            WeatherSeverity.Harsh => Harsh,
            _ => SeverityEffect.None
        };

        public WeatherSeverity RollSeverity()
        {
            float roll = UnityEngine.Random.value;
            if (roll < MildChance) return WeatherSeverity.Mild;
            if (roll < MildChance + HarshChance) return WeatherSeverity.Harsh;
            return WeatherSeverity.Normal;
        }

        [Header("Transport services")]
        [Tooltip("Multiplies the seasonal delay chance per service — slow services are more exposed to the weather.")]
        [Min(0f)] public float EconomyDelayFactor = 1.25f;
        [Min(0f)] public float StandardDelayFactor = 1f;
        [Min(0f)] public float ExpressDelayFactor = 0.5f;

        public SeasonProfile Get(Season season) => season switch
        {
            Season.Spring => Spring,
            Season.Summer => Summer,
            Season.Autumn => Autumn,
            Season.Winter => Winter,
            _ => Spring
        };

        public float GetDelayFactor(TransportOption option) => option switch
        {
            TransportOption.Economy => EconomyDelayFactor,
            TransportOption.Express => ExpressDelayFactor,
            _ => StandardDelayFactor
        };

        /// <summary>Chance a shipment due in this season is held up one week.</summary>
        public float GetDelayChance(Season season, ShippingZone zone, TransportOption option) =>
            Mathf.Clamp01(Get(season).GetOdds(zone).DelayChance * GetDelayFactor(option));

        /// <summary>Chance a shipment due in this season arrives one week early.</summary>
        public float GetEarlyChance(Season season, ShippingZone zone) =>
            Mathf.Clamp01(Get(season).GetOdds(zone).EarlyChance);

        public float GetDamageChanceMultiplier(Season season) => Mathf.Max(0f, Get(season).DamageChanceMultiplier);

        public float GetTransportCostMultiplier(Season season) => Mathf.Max(0f, Get(season).TransportCostMultiplier);

        // ---------------------------------------------------------------- market (blended)

        /// <summary>Market conditions on one date: a blend of the two seasons around it.</summary>
        public readonly struct MarketConditions
        {
            private readonly SeasonProfile _from;
            private readonly SeasonProfile _to;

            /// <summary>0 = fully the earlier season, 1 = fully the later one.</summary>
            public readonly float Blend;

            public MarketConditions(SeasonProfile from, SeasonProfile to, float blend)
            {
                _from = from;
                _to = to;
                Blend = Mathf.Clamp01(blend);
            }

            public float NpcActivity => Mix(p => p.NpcActivity);
            public float CollectorChance => Mix(p => p.CollectorChance);
            public float NewListings => Mix(p => p.NewListings);
            public float DemandMultiplier(AntiqueType type) => Mix(p => p.GetDemandMultiplier(type));

            private float Mix(Func<SeasonProfile, float> value)
            {
                if (_from == null || _to == null) return 1f;
                return Mathf.Max(0f, Mathf.Lerp(value(_from), value(_to), Blend));
            }
        }

        // Mid-season days of the year (15 Jan, 15 Apr, 15 Jul, 15 Oct): a season's market numbers peak there.
        private static readonly (Season Season, int DayOfYear)[] Peaks =
        {
            (Season.Winter, 15),
            (Season.Spring, 105),
            (Season.Summer, 196),
            (Season.Autumn, 288)
        };

        /// <summary>
        /// Market conditions on a date, blended smoothly between the peaks of the two seasons around it
        /// (e.g. early December is mostly autumn turning into winter).
        /// </summary>
        public MarketConditions GetMarketConditions(DateTime date)
        {
            int dayOfYear = date.DayOfYear;

            for (int i = 0; i < Peaks.Length; i++)
            {
                var from = Peaks[i];
                var to = Peaks[(i + 1) % Peaks.Length];

                int start = from.DayOfYear;
                int end = to.DayOfYear > start ? to.DayOfYear : to.DayOfYear + 365;
                int current = dayOfYear >= start ? dayOfYear : dayOfYear + 365;

                if (current < start || current >= end) continue;

                float t = (current - start) / (float)(end - start);
                return new MarketConditions(Get(from.Season), Get(to.Season), Mathf.SmoothStep(0f, 1f, t));
            }

            return new MarketConditions(Get(Season.Winter), Get(Season.Winter), 0f);
        }
    }
}
