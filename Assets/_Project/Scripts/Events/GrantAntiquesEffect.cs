using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public class GrantAntiquesEffect : EventEffect
    {
        [Header("Quantity")]
        [Tooltip("Minimum number of antiques granted.")]
        public int MinCount = 1;
        [Tooltip("Maximum number of antiques granted.")]
        public int MaxCount = 1;

        [Header("Antique type")]
        [Tooltip("If true, each granted antique's type is picked at random. If false, only AntiqueType below is used.")]
        public bool RandomizeAntiqueType = true;
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Header("Century")]
        [Tooltip("If true, each granted antique's century is picked at random. If false, only Century below is used.")]
        public bool RandomizeCentury = true;
        public Century Century = Century.Unknown;

        [Header("Country")]
        [Tooltip("If true, each granted antique's country is picked at random. If false, only Country below is used.")]
        public bool RandomizeCountry = true;
        public Country Country = Country.Other;

        [Header("Quality")]
        [Tooltip("If true, each granted antique's Condition is rolled randomly within its normal range. " +
                 "If false, every granted antique uses the fixed Quality value below.")]
        public bool RandomizeQuality = true;
        [Range(Antique.MinCondition, Antique.MaxCondition)]
        public float Quality = Antique.MaxCondition;

        public override void Apply(EventContext context)
        {
            var playerInventory = context.PlayerInventory;
            if (playerInventory == null)
            {
                Debug.LogWarning("GrantAntiquesEffect: no player inventory available — nothing granted.");
                return;
            }

            int min = Mathf.Max(0, Mathf.Min(MinCount, MaxCount));
            int max = Mathf.Max(min, MaxCount);
            int count = UnityEngine.Random.Range(min, max + 1);

            int granted = 0;
            for (int i = 0; i < count; i++)
            {
                if (GrantOne(playerInventory))
                    granted++;
            }

            Debug.Log($"GrantAntiquesEffect: granted {granted}/{count} antique(s) to the player.");
        }

        public override void Revert(EventContext context)
        {
        }

        public override NewsEventData CreateNewsData()
        {
            return new NewsEventData(TargetScope.Other, AntiqueType, Country, Century, true);
        }

        public override EventEffect Clone()
        {
            return new GrantAntiquesEffect
            {
                MinCount = MinCount,
                MaxCount = MaxCount,
                RandomizeAntiqueType = RandomizeAntiqueType,
                AntiqueType = AntiqueType,
                RandomizeCentury = RandomizeCentury,
                Century = Century,
                RandomizeCountry = RandomizeCountry,
                Country = Country,
                RandomizeQuality = RandomizeQuality,
                Quality = Quality
            };
        }

        private bool GrantOne(TraderInventory playerInventory)
        {
            AntiqueDefinition definition = PickDefinition();
            if (definition == null) return false;

            float condition = RandomizeQuality
                ? UnityEngine.Random.Range(Antique.MinCondition, Antique.MaxCondition)
                : Mathf.Clamp(Quality, Antique.MinCondition, Antique.MaxCondition);

            float priceFactor = UnityEngine.Random.Range(Antique.MinPriceFactor, Antique.MaxPriceFactor);

            var antique = new Antique(definition.Id, condition, priceFactor, ownerId: Antique.PlayerOwnerId);
            return playerInventory.GrantHolding(antique);
        }

        private AntiqueDefinition PickDefinition()
        {
            List<AntiqueDefinition> pool = AntiqueDatabase.GetAll();

            if (!RandomizeAntiqueType)
                pool = pool.Where(d => d.Type == AntiqueType).ToList();
            if (!RandomizeCentury)
                pool = pool.Where(d => d.Century == Century).ToList();
            if (!RandomizeCountry)
                pool = pool.Where(d => d.Country == Country).ToList();

            if (pool.Count == 0)
            {
                Debug.LogWarning("GrantAntiquesEffect: no AntiqueDefinition matches the configured filters — skipping one item.");
                return null;
            }

            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }
    }
}
