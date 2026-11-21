using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Saving;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AntiqueTradingSimulator.Autoplay
{
    /// <summary>
    /// Starts an autoplay run when the game is launched with -autoplay (or from the editor's
    /// Autoplay window): seeds the randomness, applies profile/static overrides, skips the main
    /// menu into a new game and puts an <see cref="AutoplayDriver"/> into the game scene.
    /// Without -autoplay it does nothing.
    /// </summary>
    public static class AutoplayBootstrap
    {
        public static AutoplayConfig Config { get; private set; }

        /// <summary>Results of every override, early and scene ones, for the run summary.</summary>
        public static readonly List<AutoplayOverrides.Result> OverrideResults = new List<AutoplayOverrides.Result>();

        private static List<string> _sceneOverrides = new List<string>();
        private static bool _driverStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            Config = AutoplayConfig.Read();
            if (!Config.Active) return;

            OverrideResults.Clear();
            _driverStarted = false;

            // Same seed as the diagnostics (-atsSeed). Done here as well because TelemetrySeed only
            // seeds when ATS_DIAGNOSTICS is defined, and the editor window can't pass a command line.
            if (Config.HasSeed)
            {
                Random.InitState(Config.Seed);
                Core.IdFactory.Seed(Config.Seed);
            }

            // Fast, unattended, in the background.
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            OverrideResults.AddRange(AutoplayOverrides.ApplyEarlyOverrides(Config.Overrides, out _sceneOverrides));

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            Debug.Log("===== ATS AUTOPLAY: ON =====\n" +
                      $"  strategy : {Config.Strategy}\n" +
                      $"  seed     : {(Config.HasSeed ? Config.Seed.ToString() : "none (runs will differ)")}\n" +
                      $"  weeks    : {(Config.MaxWeeks > 0 ? Config.MaxWeeks.ToString() : "whole campaign")}\n" +
                      $"  overrides: {Config.Overrides.Count}\n" +
                      $"  args     : {string.Join(" ", Config.Arguments)}");
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Config == null || !Config.Active || _driverStarted) return;

            // The menu: go straight into a new game.
            if (scene.name == GameSession.MainMenuScene)
            {
                // Re-seed so whatever the menu drew from Random can't shift the game's rolls.
                if (Config.HasSeed)
                {
                    Random.InitState(Config.Seed);
                    Core.IdFactory.Seed(Config.Seed);
                }
                GameSession.StartNewGame();
                return;
            }

            if (Object.FindFirstObjectByType<EconomyManager>() == null) return;

            _driverStarted = true;
            OverrideResults.AddRange(AutoplayOverrides.ApplySceneOverrides(_sceneOverrides));

            var host = new GameObject(nameof(AutoplayDriver));
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<AutoplayDriver>().Init(Config);
        }
    }
}
