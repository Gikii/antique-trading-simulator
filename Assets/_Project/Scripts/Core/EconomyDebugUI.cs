using UnityEngine;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// TEMPORARY debug UI (IMGUI) for manually testing the economy prototype.
    /// Shows current day and market state, with buttons to advance time and trigger trades.
    /// Remove or replace with proper UI once the game has a real UI system.
    /// </summary>
    [RequireComponent(typeof(TimeManager))]
    public class EconomyDebugUI : MonoBehaviour
    {
        [SerializeField] private string testAntiqueId = "vase_001";
        [SerializeField] private int fontSize = 24;
        [SerializeField] private int panelWidth = 500;
        [SerializeField] private int panelHeight = 420;

        private Market.Market _market;
        private Antique _testAntique;
        private TimeManager _timeManager;

        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _boxStyle;

        void Start()
        {
            _timeManager = GetComponent<TimeManager>();

            _market = new Market.Market();
            _testAntique = new Antique(testAntiqueId, initialSupply: 10f, initialDemand: 5f);
            _market.AddAntique(_testAntique);
        }

        private void SetupStyles()
        {
            if (_labelStyle != null) return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                padding = new RectOffset(10, 10, 6, 6)
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                padding = new RectOffset(10, 10, 12, 12)
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = fontSize
            };
        }

        void OnGUI()
        {
            SetupStyles();

            GUILayout.BeginArea(new Rect(20, 20, panelWidth, panelHeight), _boxStyle);

            GUILayout.Label($"Day: {_timeManager.CurrentDay}", _labelStyle);

            if (_testAntique != null)
            {
                GUILayout.Space(10);
                GUILayout.Label(_testAntique.Name, _labelStyle);
                GUILayout.Label($"Price: {_testAntique.CurrentPrice:F2}", _labelStyle);
                GUILayout.Label($"Supply: {_testAntique.Supply:F1}", _labelStyle);
                GUILayout.Label($"Demand: {_testAntique.Demand:F1}", _labelStyle);
            }

            GUILayout.Space(20);

            if (GUILayout.Button("Advance Day", _buttonStyle))
            {
                _timeManager.ForceAdvanceDay();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Buy", _buttonStyle))
            {
                _market.Buy(_testAntique.Id);
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Sell", _buttonStyle))
            {
                _market.Sell(_testAntique.Id);
            }

            GUILayout.EndArea();
        }
    }
}