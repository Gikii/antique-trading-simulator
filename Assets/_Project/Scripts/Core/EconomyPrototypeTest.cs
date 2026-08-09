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
        [SerializeField] private string testAntiqueId = "vase_001";

        private Market.Market _market;
        private Antique _testAntique;
        private TimeManager _timeManager;

        void Start()
        {
            _timeManager = GetComponent<TimeManager>();
            _timeManager.OnDayChanged += HandleDayChanged;

            _market = new Market.Market();
            _testAntique = new Antique(testAntiqueId, initialSupply: 10f, initialDemand: 5f);
            _market.AddAntique(_testAntique);

            Debug.Log($"Day 1 start: {_testAntique}");
        }

        private void HandleDayChanged(int newDay)
        {
            if (newDay % 2 == 0)
                _market.Buy(_testAntique.Id);
            else
                _market.Sell(_testAntique.Id);

            Debug.Log($"Day {newDay}: {_testAntique}");
        }

        void OnDestroy()
        {
            if (_timeManager != null)
                _timeManager.OnDayChanged -= HandleDayChanged;
        }
    }
}