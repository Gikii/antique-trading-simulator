using UnityEngine;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// TEMPORARY script for manually testing the economy engine over time.
    /// Remove once the real EconomyManager exists.
    /// </summary>
    [RequireComponent(typeof(TimeManager))]
    public class EconomyPrototypeTest : MonoBehaviour
    {
        [SerializeField] private string testDefinitionId = "vase_001";

        private Market.Market _market;
        private Antique _testListing;
        private TimeManager _timeManager;

        void Start()
        {
            _timeManager = GetComponent<TimeManager>();
            _timeManager.OnDayChanged += HandleDayChanged;

            _market = new Market.Market();
            _market.RegisterType(testDefinitionId, initialSupply: 10f, initialDemand: 5f);

            _testListing = new Antique(testDefinitionId, quality: 1f, state: 1f);
            _market.AddListing(_testListing);

            Debug.Log($"Day 1 start: {_testListing}");
        }

        private void HandleDayChanged(int newDay)
        {
            if (newDay % 2 == 0)
            {
                _market.Buy(_testListing.Id);
            }
            else
            {
                _market.Sell(_testListing);
            }

            Debug.Log($"Day {newDay}: {_testListing}");
        }

        void OnDestroy()
        {
            if (_timeManager != null)
                _timeManager.OnDayChanged -= HandleDayChanged;
        }
    }
}
