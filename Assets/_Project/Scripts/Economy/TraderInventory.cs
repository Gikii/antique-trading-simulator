using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Tracks a trader's (player or NPC) cash and antique holdings, and wraps
    /// Market.Buy/Sell so cash and inventory only ever change together with a successful trade
    /// </summary>
    [Serializable]
    public class TraderInventory
    {
        public float Cash { get; private set; }

        private readonly Dictionary<string, float> _holdings = new Dictionary<string, float>();
        public IReadOnlyDictionary<string, float> Holdings => _holdings;

        public event Action<float> OnCashChanged;
        public event Action<string, float> OnHoldingChanged; // (antiqueId, newAmountOwned)

        public TraderInventory(float startingCash = 0f)
        {
            Cash = startingCash;
        }

        public float GetAmountOwned(string antiqueId)
        {
            _holdings.TryGetValue(antiqueId, out float amount);
            return amount;
        }


        public bool Buy(Market.Market market, string antiqueId, float amount)
        {
            var antique = market.GetById(antiqueId);
            if (antique == null) return false;

            float cost = antique.CurrentPrice * amount;
            if (cost > Cash) return false;

            if (!market.Buy(antiqueId, amount)) return false;

            Cash -= cost;
            _holdings.TryGetValue(antiqueId, out float owned);
            _holdings[antiqueId] = owned + amount;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(antiqueId, _holdings[antiqueId]);
            return true;
        }

        public bool Sell(Market.Market market, string antiqueId, float amount)
        {
            _holdings.TryGetValue(antiqueId, out float owned);
            if (owned < amount) return false;

            var antique = market.GetById(antiqueId);
            if (antique == null) return false;

            market.Sell(antiqueId, amount);

            Cash += antique.CurrentPrice * amount;
            _holdings[antiqueId] = owned - amount;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(antiqueId, _holdings[antiqueId]);
            return true;
        }
    }
}
