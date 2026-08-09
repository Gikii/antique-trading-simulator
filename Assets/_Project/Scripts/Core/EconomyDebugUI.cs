using UnityEngine;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// TEMPORARY debug UI (IMGUI) for testing the economy simulation.
    /// Reads market state from EconomyManager and lets you advance time,
    /// pause/resume the clock, or trigger trades on any antique on the market.
    /// </summary>
    [RequireComponent(typeof(TimeManager))]
    [RequireComponent(typeof(EconomyManager))]
    public class EconomyDebugUI : MonoBehaviour
    {
        [SerializeField] private int fontSize = 20;
        [SerializeField] private int panelWidth = 600;
        [SerializeField] private int panelHeight = 750;

        private TimeManager _timeManager;
        private EconomyManager _economyManager;

        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _boxStyle;

        private Vector2 _scrollPos;

        void Start()
        {
            _timeManager = GetComponent<TimeManager>();
            _economyManager = GetComponent<EconomyManager>();
        }

        private void SetupStyles()
        {
            if (_labelStyle != null) return;

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, padding = new RectOffset(6, 6, 4, 4) };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize, padding = new RectOffset(8, 8, 8, 8) };
            _boxStyle = new GUIStyle(GUI.skin.box) { fontSize = fontSize };
        }

        void OnGUI()
        {
            SetupStyles();

            GUILayout.BeginArea(new Rect(20, 20, panelWidth, panelHeight), _boxStyle);

            GUILayout.Label($"Day: {_timeManager.CurrentDay}", _labelStyle);
            GUILayout.Label($"Next day in: {_timeManager.TimeUntilNextDay:F1}s", _labelStyle);
            GUILayout.Label($"Status: {(_timeManager.IsRunning ? "Running" : "Paused")}", _labelStyle);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(_timeManager.IsRunning ? "Stop" : "Start", _buttonStyle))
            {
                _timeManager.ToggleRunning();
            }

            if (GUILayout.Button("Advance Day", _buttonStyle))
            {
                _timeManager.ForceAdvanceDay();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(15);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            foreach (var antique in _economyManager.Market.Antiques)
            {
                GUILayout.BeginVertical(_boxStyle);

                GUILayout.Label(antique.Name, _labelStyle);
                GUILayout.Label($"Price: {antique.CurrentPrice:F2} | Supply: {antique.Supply:F1} | Demand: {antique.Demand:F1}", _labelStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Buy", _buttonStyle))
                {
                    _economyManager.Market.Buy(antique.Id);
                }
                if (GUILayout.Button("Sell", _buttonStyle))
                {
                    _economyManager.Market.Sell(antique.Id);
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.Space(8);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}