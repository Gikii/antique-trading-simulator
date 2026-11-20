using System;
using System.IO;
using AntiqueTradingSimulator.Economy;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Decides whether to record, says so out loud, and puts a TelemetryRecorder into the game scene.
    /// </summary>
    public static class TelemetryBootstrap
    {
        /// <summary>How long between checks for a game scene while no session is open.</summary>
        private const float PollSeconds = 0.5f;

        private static Watchdog _watchdog;
        private static int _sessions;

        /// <summary>Whether the main menu has been seen, which tells a new game from a cold start.</summary>
        public static bool MenuSeen { get; private set; }

        /// <summary>Sessions begun in this process, 1-based. Used in the folder name.</summary>
        public static int NextSessionIndex() => ++_sessions;

        /// <summary>Logs a line per game week instead of one every ten. -atsVerbose.</summary>
        public static bool Verbose { get; private set; }

        /// <summary>True when ATS_DIAGNOSTICS was defined for this assembly.</summary>
        public static bool DefineActive =>
#if ATS_DIAGNOSTICS
            true;
#else
            false;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Announce()
        {
            string why;
            bool on = ShouldRecord(out why);

            string tables = DefineActive
                ? "all 34 tables"
                : "31 of 34 tables (fact_npc_action, fact_browse_funnel and fact_news_reception " +
                  "need ATS_DIAGNOSTICS, which strips their call sites when undefined)";

            if (on)
            {
                Debug.Log(
                    "===== ATS DIAGNOSTICS: ON =====\n" +
                    $"  reason          : {why}\n" +
                    $"  ATS_DIAGNOSTICS : {(DefineActive ? "defined" : "NOT defined")}\n" +
                    $"  recording       : {tables}\n" +
                    $"  mirrors         : {(Telemetry.Mirrors ? "on" : "off")}\n" +
                    $"  seed            : {(TelemetrySeed.Applied ? TelemetrySeed.Seed.ToString() : "not seeded (runs will differ)")}\n" +
                    $"  output root     : {OutputRoot()}\n" +
                    "  waiting for a scene with an EconomyManager; a session starts when one appears.");
            }
            else
            {
                Debug.Log(
                    "===== ATS DIAGNOSTICS: OFF =====\n" +
                    $"  reason          : {why}\n" +
                    $"  ATS_DIAGNOSTICS : {(DefineActive ? "defined" : "NOT defined")}\n" +
                    "  to switch on: define ATS_DIAGNOSTICS in Player settings, pass -atsDiagnostics, " +
                    "set ATS_DIAGNOSTICS_ON=1, or set Telemetry.Enabled = true before the scene loads.");
                return;
            }

            Install();
        }

        /// <summary>
        /// Recording is on in the editor unless opted out, and in a player only when asked for.
        /// A build should not write diagnostics by accident.
        /// </summary>
        private static bool ShouldRecord(out string why)
        {
            if (!Telemetry.Enabled)
            {
                why = "Telemetry.Enabled was set to false in code";
                return false;
            }

            if (Switch("-noDiagnostics", "ATS_DIAGNOSTICS_OFF"))
            {
                why = "turned off by -noDiagnostics or ATS_DIAGNOSTICS_OFF";
                Telemetry.Enabled = false;
                return false;
            }

            if (Switch("-noDiagnosticsMirrors", "ATS_DIAGNOSTICS_NO_MIRRORS")) Telemetry.Mirrors = false;
            if (Switch("-atsVerbose", "ATS_VERBOSE")) Verbose = true;

            if (Switch("-atsDiagnostics", "ATS_DIAGNOSTICS_ON"))
            {
                why = "asked for by -atsDiagnostics or ATS_DIAGNOSTICS_ON";
                return true;
            }

            if (DefineActive)
            {
                why = "ATS_DIAGNOSTICS is defined";
                return true;
            }

            if (Application.isEditor)
            {
                why = "running in the editor, where recording is on by default";
                return true;
            }

            why = "a player build with neither ATS_DIAGNOSTICS nor -atsDiagnostics";
            Telemetry.Enabled = false;
            return false;
        }

        private static bool Switch(string argument, string variable)
        {
            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                    if (string.Equals(arg, argument, StringComparison.OrdinalIgnoreCase)) return true;

                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Where session folders are created. -atsOutput:&lt;path&gt; overrides it.</summary>
        public static string OutputRoot()
        {
            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (arg == null || !arg.StartsWith("-atsOutput:", StringComparison.OrdinalIgnoreCase)) continue;

                    string custom = arg.Substring("-atsOutput:".Length).Trim('"');
                    if (!string.IsNullOrEmpty(custom)) return custom;
                }

                string env = Environment.GetEnvironmentVariable("ATS_OUTPUT");
                if (!string.IsNullOrEmpty(env)) return env;
            }
            catch
            {
                // Fall through to the default.
            }

            return Path.Combine(Application.persistentDataPath, "diagnostics");
        }

        private static void Install()
        {
            if (_watchdog != null) return;

            SceneManager.sceneLoaded -= NoteScene;
            SceneManager.sceneLoaded += NoteScene;

            var host = new GameObject("TelemetryBootstrap");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.DontSave;
            _watchdog = host.AddComponent<Watchdog>();
        }

        private static void NoteScene(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == Saving.GameSession.MainMenuScene) MenuSeen = true;
        }

        /// <summary>
        /// Lives across scene loads and spawns a recorder whenever a scene turns up with an
        /// EconomyManager and no session running.
        /// </summary>
        [DefaultExecutionOrder(1500)]
        private sealed class Watchdog : MonoBehaviour
        {
            private float _nextCheck;
            private int _lastEconomyId;
            private int _sessionsSeen;

            private void Update()
            {
                if (TelemetryRecorder.Instance != null) return;
                if (Time.unscaledTime < _nextCheck) return;
                _nextCheck = Time.unscaledTime + PollSeconds;

                var scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return;

                var economy = UnityEngine.Object.FindFirstObjectByType<EconomyManager>();
                if (economy == null) return;

                // Keyed on the manager, not the scene: a world already recorded is not recorded
                // again, so a session that ended at the score screen does not restart while the
                // player reads it, while a reloaded scene builds a new manager and so a new
                // session. Scene handles can be reused; a fresh object cannot.
                int economyId = economy.GetInstanceID();
                if (economyId == _lastEconomyId) return;

                _lastEconomyId = economyId;
                _sessionsSeen++;

                Debug.Log($"ATS diagnostics: found the game in scene '{scene.name}' — starting session {_sessionsSeen}.");

                var host = new GameObject(nameof(TelemetryRecorder));
                SceneManager.MoveGameObjectToScene(host, scene);
                host.AddComponent<TelemetryRecorder>();
            }
        }
    }
}
