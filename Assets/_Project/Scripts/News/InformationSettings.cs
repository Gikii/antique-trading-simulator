using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Events;
using UnityEngine;

namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// How likely each kind of news is to reach a trader, and how late it arrives, per Information Network level.
    /// </summary>
    [CreateAssetMenu(fileName = "InformationSettings", menuName = "AntiqueTradingSimulator/Information Settings")]
    public class InformationSettings : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "InformationSettings";

        /// <summary>Number of levels (InfoAccessLevel.LocalPress … InternationalNetwork).</summary>
        public const int LevelCount = 5;

        /// <summary>
        /// Chance curve and delay table for one news type. 
        ///     g    = (oN / o1) ^ (1 / (N - 1)),  where o = p / (1 - p)
        ///     p(L) = o1·g^(L-1) / (1 + o1·g^(L-1))
        /// </summary>
        [Serializable]
        public class AccessCurve
        {
            public NewsType Type;

            [Tooltip("Share of published items of this type that reach a level 1 trader.")]
            [Range(0f, 1f)] public float TierOneChance = 0.10f;

            [Tooltip("Same at the top tier; the tiers between are interpolated.")]
            [Range(0f, 1f)] public float TopTierChance = 0.75f;

            [Tooltip("Weeks between publication and this level hearing about it.")]
            public Vector2Int[] DelayWeeksPerLevel =
            {
                new Vector2Int(1, 3),
                new Vector2Int(1, 2),
                new Vector2Int(0, 2),
                new Vector2Int(0, 1),
                new Vector2Int(0, 0)
            };

            // Growth factor, recomputed whenever the anchors change.
            [NonSerialized] private float _growth;
            [NonSerialized] private float _growthForFloor = float.NaN;
            [NonSerialized] private float _growthForCeiling = float.NaN;

            /// <summary>Chance an item of this type reaches a trader.</summary>
            public float ChanceAt(InfoAccessLevel level)
            {
                float floor = Mathf.Clamp01(TierOneChance);
                float ceiling = Mathf.Clamp01(TopTierChance);

                if (floor >= 1f) return 1f;
                if (floor <= 0f) return 0f;

                if (!Mathf.Approximately(floor, _growthForFloor) ||
                    !Mathf.Approximately(ceiling, _growthForCeiling))
                {
                    _growth = Mathf.Pow(Odds(ceiling) / Odds(floor), 1f / (LevelCount - 1));
                    _growthForFloor = floor;
                    _growthForCeiling = ceiling;
                }

                int index = Mathf.Clamp((int)level, 1, LevelCount) - 1;
                float odds = Odds(floor) * Mathf.Pow(_growth, index);
                return odds / (1f + odds);
            }

            /// <summary>Min/max weeks of delay at level, defaulting to none.</summary>
            public Vector2Int DelayAt(InfoAccessLevel level)
            {
                if (DelayWeeksPerLevel == null || DelayWeeksPerLevel.Length == 0)
                    return Vector2Int.zero;

                int index = Mathf.Clamp((int)level, 1, DelayWeeksPerLevel.Length) - 1;
                var range = DelayWeeksPerLevel[index];
                int min = Mathf.Max(0, Mathf.Min(range.x, range.y));
                int max = Mathf.Max(min, Mathf.Max(range.x, range.y));
                return new Vector2Int(min, max);
            }

            private static float Odds(float p)
            {
                p = Mathf.Clamp(p, 0.0001f, 0.9999f);
                return p / (1f - p);
            }
        }

        [Tooltip("One entry per news type; a type with no entry is always delivered immediately.")]
        public List<AccessCurve> Curves = new List<AccessCurve>
        {
            // Official news is public, so everyone gets it the week it prints.
            new AccessCurve
            {
                Type = NewsType.Official,
                TierOneChance = 1f,
                TopTierChance = 1f,
                DelayWeeksPerLevel = new[]
                {
                    Vector2Int.zero, Vector2Int.zero, Vector2Int.zero, Vector2Int.zero, Vector2Int.zero
                }
            },

            // 25 / 40 / 58 / 74 / 85 per level.
            new AccessCurve
            {
                Type = NewsType.Rumor,
                TierOneChance = 0.25f,
                TopTierChance = 0.85f
            },

            // 10 / 20 / 37 / 57 / 75 per level.
            new AccessCurve
            {
                Type = NewsType.Leak,
                TierOneChance = 0.10f,
                TopTierChance = 0.75f
            },

            // Misinformation travels like a rumour; nothing produces it yet.
            new AccessCurve
            {
                Type = NewsType.Fake,
                TierOneChance = 0.25f,
                TopTierChance = 0.85f
            }
        };

        [Header("Dry-spell protection")]
        [Tooltip("After this many times the average gap between hits (1 / chance), the next item of that type is delivered outright means a tier that hears one leak in ten never misses more than thirty in a row.")]
        [Range(0f, 10f)] public float GuaranteedAfterExpectedGaps = 3f;

        // ---------------------------------------------------------------- lookup

        public AccessCurve CurveFor(NewsType type)
        {
            if (Curves == null) return null;

            foreach (var curve in Curves)
                if (curve != null && curve.Type == type)
                    return curve;

            return null;
        }

        /// <summary>Chance of a news item reaching a trader at information access level/>.</summary>
        public float ChanceFor(NewsType type, InfoAccessLevel level)
        {
            var curve = CurveFor(type);
            return curve != null ? curve.ChanceAt(level) : 1f;
        }

        /// <summary>Min/max weeks of delay for news item of type at information access level/>.</summary>
        public Vector2Int DelayFor(NewsType type, InfoAccessLevel level)
        {
            var curve = CurveFor(type);
            return curve != null ? curve.DelayAt(level) : Vector2Int.zero;
        }

        /// <summary>
        /// How many of this type a trader may miss in a row before the next is delivered outright, or int.MaxValue when the protection is off.
        /// </summary>
        public int ForcedDeliveryAfterMisses(NewsType type, InfoAccessLevel level)
        {
            if (GuaranteedAfterExpectedGaps <= 0f) return int.MaxValue;

            float chance = ChanceFor(type, level);
            if (chance <= 0f) return int.MaxValue;
            if (chance >= 1f) return 1;

            return Mathf.Max(1, Mathf.CeilToInt(GuaranteedAfterExpectedGaps / chance));
        }

        // ---------------------------------------------------------------- loading

        private static InformationSettings _cached;

        /// <summary>The asset from Resources, or the defaults above if none has been created yet.</summary>
        public static InformationSettings Active
        {
            get
            {
                if (_cached == null)
                    _cached = Resources.Load<InformationSettings>(ResourcePath);

                if (_cached == null)
                    _cached = CreateInstance<InformationSettings>();

                return _cached;
            }
        }

        /// <summary>Publishes the instance assigned to NewsManager, so the UI reads the same numbers.</summary>
        public static void SetActive(InformationSettings settings)
        {
            if (settings != null) _cached = settings;
        }
    }
}
