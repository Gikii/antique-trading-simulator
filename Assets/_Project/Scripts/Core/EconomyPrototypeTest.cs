using UnityEngine;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// TEMPORARY script for manually testing the economy engine.
    /// </summary>
    public class EconomyPrototypeTest : MonoBehaviour
    {
        private Market.Market _market;

        void Start()
        {
            _market = new Market.Market();

            var vase = new Antique("vase_001", "Chinese vase", "Porcelain", basePrice: 500f, initialSupply: 10f, initialDemand: 5f);
            _market.AddAntique(vase);

            Debug.Log($"Start: {vase}");

            // Simulate several consecutive purchases — price should rise
            for (int i = 0; i < 5; i++)
            {
                _market.Buy("vase_001");
                Debug.Log($"After purchase {i + 1}: {vase}");
            }

            // Simulate selling — price should start dropping
            for (int i = 0; i < 3; i++)
            {
                _market.Sell("vase_001");
                Debug.Log($"After sale {i + 1}: {vase}");
            }
        }
    }
}