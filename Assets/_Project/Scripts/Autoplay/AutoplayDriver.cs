using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Diagnostics;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Saving;
using UnityEngine;

namespace AntiqueTradingSimulator.Autoplay
{
    /// <summary>
    /// Plays the campaign without a person: holds the clock and steps it tick by tick
    /// (TicksPerFrame ticks per frame, as fast as the machine allows), turns down every auction
    /// (no real-time countdowns), lets the strategy act at the start of each in-game day, and
    /// when the campaign ends (or MaxWeeks is reached) writes autoplay_summary.json next to the
    /// diagnostics CSVs and, with -autoplayQuit, quits.
    ///
    /// Stepping ticks instead of letting the clock run keeps a run identical at any frame rate,
    /// so the same seed and arguments give the same game. Autosaves are switched off so a run
    /// never overwrites the player's autosave slots.
    /// </summary>
    public class AutoplayDriver : MonoBehaviour
    {
        /// <summary>How long to wait for the diagnostics to start their session before playing anyway.</summary>
        private const float TelemetryWaitSeconds = 15f;

        private AutoplayConfig _config;
        private IAutoplayStrategy _strategy;
        private AutoplayContext _context;

        private EconomyManager _economy;
        private Core.TimeManager _time;
        private PlayerTrader _player;
        private AuctionManager _auctions;
        private CampaignManager _campaign;

        private string _outputDirectory;
        private StreamWriter _actions;
        private int _actionCount;
        private int _ticksPlayed;
        private int _auctionsDeclined;
        private float _startedAt;
        private float _waitStartedAt;
        private int _startFrame;
        private bool _playing;
        private bool _finished;
        private string _fatal;

        public void Init(AutoplayConfig config) => _config = config;

        private void Start()
        {
            _waitStartedAt = Time.realtimeSinceStartup;
            _startFrame = Time.frameCount;

            _economy = FindFirstObjectByType<EconomyManager>();
            _time = _economy != null ? _economy.TimeManager : FindFirstObjectByType<Core.TimeManager>();
            _player = FindFirstObjectByType<PlayerTrader>();
            _auctions = FindFirstObjectByType<AuctionManager>();
            _campaign = FindFirstObjectByType<CampaignManager>();

            var saves = FindFirstObjectByType<SaveManager>();
            if (saves != null) saves.enabled = false;

            if (_time != null) _time.Pause();

            if (_config == null) _fatal = "no autoplay config";
            else if (_economy == null || _time == null) _fatal = "no EconomyManager or TimeManager in the scene";
            else if (_player == null) _fatal = "no PlayerTrader in the scene";

            foreach (var result in AutoplayBootstrap.OverrideResults)
                if (!result.Applied) _fatal = $"override failed: {result.Line} ({result.Message})";

            if (_fatal == null)
            {
                _strategy = AutoplayStrategies.Create(_config, out string error);
                if (_strategy == null) _fatal = error;
            }
        }

        private void Update()
        {
            if (_finished) return;

            if (_fatal != null)
            {
                Finish("error: " + _fatal, exitCode: 3);
                return;
            }

            if (!_playing && !TryBeginPlaying()) return;

            for (int i = 0; i < _config.TicksPerFrame; i++)
            {
                if (IsOver(out string reason))
                {
                    Finish(reason, exitCode: 0);
                    return;
                }

                // A screen that resumed the clock (the end screen, a dialog) must not run it alongside us.
                if (_time.IsRunning) _time.Pause();

                try
                {
                    StepOnce();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Finish($"error: {e.GetType().Name}: {e.Message}", exitCode: 4);
                    return;
                }
            }
        }

        /// <summary>Waits for the diagnostics session (so week 1 is recorded), then sets up the run.</summary>
        private bool TryBeginPlaying()
        {
            // Let every manager run its first Update (the clock raises the week's first tick there).
            if (Time.frameCount < _startFrame + 2) return false;

            bool telemetryExpected = Telemetry.Enabled;
            bool telemetryReady = TelemetryRecorder.Instance != null && TelemetryRecorder.Instance.Run != null;
            if (telemetryExpected && !telemetryReady && Time.realtimeSinceStartup - _waitStartedAt < TelemetryWaitSeconds)
                return false;

            if (telemetryExpected && !telemetryReady)
                Debug.LogWarning("Autoplay: the diagnostics session did not start in time — playing without it.");

            _outputDirectory = telemetryReady ? TelemetryRecorder.Instance.Run.Directory : FallbackDirectory();
            Directory.CreateDirectory(_outputDirectory);
            OpenActionLog();

            _context = new AutoplayContext
            {
                Market = _economy.Market,
                Player = _player,
                Config = _config,
                Log = LogAction
            };

            _startedAt = Time.realtimeSinceStartup;
            _playing = true;
            Debug.Log($"Autoplay: playing '{_strategy.Name}' from week {_time.CurrentWeek}, writing to {_outputDirectory}");
            return true;
        }

        private void StepOnce()
        {
            _time.StepTick();
            _ticksPlayed++;

            if (_auctions != null && _auctions.JoinWindowAuction != null && _auctions.DeclineAuction())
                _auctionsDeclined++;

            if (_time.CurrentTick % _time.TicksPerDay != 0) return;
            if (_campaign != null && _campaign.IsFinished) return;

            _context.Week = _time.CurrentWeek;
            _context.Tick = _time.CurrentTick;
            _strategy.OnDay(_context);
        }

        private bool IsOver(out string reason)
        {
            if (_campaign != null && _campaign.IsFinished)
            {
                reason = "campaign finished";
                return true;
            }

            if (_config.MaxWeeks > 0 && _time.CurrentWeek > _config.MaxWeeks)
            {
                reason = $"reached week {_config.MaxWeeks}";
                return true;
            }

            // A campaign with no CampaignManager still has to stop somewhere.
            if (_time.CampaignLength > 0 && _time.CurrentWeek > _time.CampaignLength + 1)
            {
                reason = "past the campaign length";
                return true;
            }

            reason = null;
            return false;
        }

        // ---------------------------------------------------------------- finishing

        private void Finish(string reason, int exitCode)
        {
            if (_finished) return;
            _finished = true;

            if (exitCode == 0) Debug.Log($"Autoplay: done — {reason}.");
            else Debug.LogError($"Autoplay: stopped — {reason}.");

            try
            {
                if (_outputDirectory == null) _outputDirectory = FallbackDirectory();
                Directory.CreateDirectory(_outputDirectory);
                File.WriteAllText(Path.Combine(_outputDirectory, "autoplay_summary.json"), Summary(reason, exitCode));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            _actions?.Dispose();
            _actions = null;

            if (_config == null || !_config.QuitWhenDone) return;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        private void OnDestroy() => _actions?.Dispose();

        private string FallbackDirectory() =>
            Path.Combine(TelemetryBootstrap.OutputRoot(), $"autoplay_{DateTime.Now:yyyyMMdd-HHmmss}");

        private string Summary(string reason, int exitCode)
        {
            var json = new JsonWriter();
            json.Value("reason", reason);
            json.Value("exit_code", exitCode);
            json.Value("strategy", _strategy != null ? _strategy.Name : _config?.Strategy);
            json.Value("seed", _config != null && _config.HasSeed ? (object)_config.Seed : null);
            json.Value("arguments", _config != null ? string.Join(" ", _config.Arguments) : "");
            json.Value("week", _time != null ? _time.CurrentWeek : 0);
            json.Value("ticks_played", _ticksPlayed);
            json.Value("auctions_declined", _auctionsDeclined);
            json.Value("player_actions", _actionCount);
            json.Value("real_seconds", _playing ? Time.realtimeSinceStartup - _startedAt : 0f);
            json.Value("long_term_demand_cap", _economy != null && _economy.Market != null ? _economy.Market.LongTermDemandCap : 0f);
            json.Value("own_trend_weight", Market.Market.OwnTrendWeight);

            json.BeginArray("overrides");
            foreach (var result in AutoplayBootstrap.OverrideResults)
                json.Item($"{(result.Applied ? "ok" : "FAILED")}: {result.Message}");
            json.EndArray();

            if (_player != null && _player.Inventory != null)
            {
                json.Value("player_cash", _player.Inventory.Cash);
                json.Value("player_wealth", _player.Inventory.Wealth);
                json.Value("player_holdings", _player.Inventory.Holdings.Count);
            }

            json.BeginArray("ranking");
            if (_campaign != null && _campaign.IsFinished)
                foreach (var standing in _campaign.FinalRanking)
                    json.Item($"{standing.Place}. {standing.Name}{(standing.IsPlayer ? " (player)" : "")} — {standing.Score.ToString("0.0", CultureInfo.InvariantCulture)}");
            json.EndArray();

            return json.ToString();
        }

        // ---------------------------------------------------------------- action log

        private void OpenActionLog()
        {
            try
            {
                _actions = new StreamWriter(Path.Combine(_outputDirectory, "autoplay_actions.csv"), false, new UTF8Encoding(false));
                _actions.WriteLine("week,tick,action,listing_id,definition_id,antique_type,amount,cash_after,note");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _actions = null;
            }
        }

        private void LogAction(string action, Antique antique, float amount, string note)
        {
            _actionCount++;
            if (_actions == null) return;

            var inv = CultureInfo.InvariantCulture;
            _actions.WriteLine(string.Join(",",
                _time.CurrentWeek.ToString(inv),
                _time.CurrentTick.ToString(inv),
                action,
                antique != null ? antique.Id : "",
                antique != null ? antique.DefinitionId : "",
                antique != null && antique.Definition != null ? antique.Definition.Type.ToString() : "",
                amount.ToString("0.####", inv),
                _player.Inventory.Cash.ToString("0.####", inv),
                Csv(note)));
        }

        private static string Csv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        /// <summary>Just enough JSON for the summary: one flat object with string arrays.</summary>
        private sealed class JsonWriter
        {
            private readonly StringBuilder _text = new StringBuilder("{\n");
            private bool _first = true;
            private bool _firstItem;

            public void Value(string key, object value)
            {
                Key(key);
                _text.Append(Literal(value));
            }

            public void BeginArray(string key)
            {
                Key(key);
                _text.Append('[');
                _firstItem = true;
            }

            public void Item(string value)
            {
                _text.Append(_firstItem ? "\n    " : ",\n    ").Append(Literal(value));
                _firstItem = false;
            }

            public void EndArray() => _text.Append(_firstItem ? "]" : "\n  ]");

            public override string ToString() => _text + "\n}\n";

            private void Key(string key)
            {
                _text.Append(_first ? "  " : ",\n  ").Append(Literal(key)).Append(": ");
                _first = false;
            }

            private static string Literal(object value)
            {
                switch (value)
                {
                    case null: return "null";
                    case bool b: return b ? "true" : "false";
                    case int i: return i.ToString(CultureInfo.InvariantCulture);
                    case float f: return float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("0.####", CultureInfo.InvariantCulture);
                    default:
                        var s = value.ToString();
                        var sb = new StringBuilder("\"");
                        foreach (char c in s)
                        {
                            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                            else if (c == '\n') sb.Append("\\n");
                            else if (c < ' ') sb.Append(' ');
                            else sb.Append(c);
                        }
                        return sb.Append('"').ToString();
                }
            }
        }
    }
}
