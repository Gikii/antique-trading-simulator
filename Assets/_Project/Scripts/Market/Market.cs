using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Holds the list of antiques available on the market and handles basic transactions.
    /// </summary>
    public class Market
    {
        private readonly List<Antique> _antiques = new List<Antique>();

        public IReadOnlyList<Antique> Antiques => _antiques;

        public void AddAntique(Antique antique)
        {
            _antiques.Add(antique);
        }

        public Antique GetById(string id)
        {
            return _antiques.Find(a => a.Id == id);
        }

        /// <summary>
        /// Player/NPC buys one unit of the antique — supply decreases, demand rises slightly (buying pressure).
        /// </summary>
        public void Buy(string id, float amount = 1f)
        {
            var antique = GetById(id);
            if (antique == null)
            {
                Debug.LogWarning($"Market: antique with ID {id} not found");
                return;
            }

            antique.Supply = Mathf.Max(0f, antique.Supply - amount);
            antique.Demand += amount * 0.1f; // buying pressure slightly increases demand

            RecalculatePrice(antique);
        }

        /// <summary>
        /// Player/NPC sells one unit of the antique — supply increases, demand drops slightly.
        /// </summary>
        public void Sell(string id, float amount = 1f)
        {
            var antique = GetById(id);
            if (antique == null)
            {
                Debug.LogWarning($"Market: antique with ID {id} not found");
                return;
            }

            antique.Supply += amount;
            antique.Demand = Mathf.Max(0f, antique.Demand - amount * 0.1f);

            RecalculatePrice(antique);
        }

        private void RecalculatePrice(Antique antique)
        {
            antique.CurrentPrice = PriceEngine.CalculatePrice(antique);
        }
    }
}