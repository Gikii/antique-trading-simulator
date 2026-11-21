using System.IO;
using System.Linq;
using AntiqueTradingSimulator.Autoplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Tools → Antique Trading Simulator → Autoplay.
    /// Runs one autoplay game in Play Mode (to watch or debug a strategy), and builds the
    /// standalone player that Tools/Autoplay/run_experiments.py starts for batches of runs.
    /// </summary>
    public class AutoplayWindow : EditorWindow
    {
        private const string BuildFolder = "Builds/Autoplay";
        private const string WindowArgsKey = "ATS.Autoplay.WindowArgs";

        private static readonly (string label, string args)[] Presets =
        {
            ("Idle (control)", "-autoplayStrategy:idle -atsSeed:1"),
            ("Buy up paintings", "-autoplayStrategy:buyup -autoplayCategory:Painting -autoplayFrom:20 -autoplayTo:45 -autoplaySell:70 -autoplayGrant:400 -atsSeed:1"),
            ("Trader", "-autoplayStrategy:trader -atsSeed:1"),
            ("Quick test (12 weeks)", "-autoplayStrategy:trader -autoplayWeeks:12 -atsSeed:1"),
        };

        private string _args;
        private bool _stopWhenDone = true;

        [MenuItem("Tools/Antique Trading Simulator/Autoplay")]
        private static void Open() => GetWindow<AutoplayWindow>("Autoplay");

        private void OnEnable() => _args = EditorPrefs.GetString(WindowArgsKey, Presets[0].args);

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Run in Play Mode", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Plays a new game by itself from the open scene (Main or MainMenu). Arguments as on the command line, " +
                "e.g. -set:speculator.TrendSensitivity=0.8. CSVs go to the diagnostics folder " +
                "(Application.persistentDataPath/diagnostics) with autoplay_summary.json and autoplay_actions.csv.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var preset in Presets)
                    if (GUILayout.Button(preset.label, EditorStyles.miniButton)) _args = preset.args;
            }

            _args = EditorGUILayout.TextArea(_args, GUILayout.MinHeight(60));
            _stopWhenDone = EditorGUILayout.ToggleLeft("Leave Play Mode when the run is over", _stopWhenDone);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Play", GUILayout.Height(28))) Play();
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Build for batch runs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Builds the game into {BuildFolder}/ for the current standalone platform with ATS_DIAGNOSTICS defined " +
                "(all 34 diagnostics tables and seeding). Player Settings are not changed. " +
                "Then run Tools/Autoplay/run_experiments.py.",
                MessageType.None);

            if (GUILayout.Button("Build autoplay player", GUILayout.Height(28))) Build();

            if (GUILayout.Button("Open diagnostics folder"))
                EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "diagnostics"));
        }

        private void Play()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string args = _args ?? "";
            if (_stopWhenDone && !args.ToLowerInvariant().Contains("-autoplayquit")) args += " -autoplayQuit";

            EditorPrefs.SetString(WindowArgsKey, _args ?? "");
            EditorPrefs.SetString(AutoplayConfig.EditorArgsKey, args.Trim());
            EditorPrefs.SetBool(AutoplayConfig.EditorRunOnceKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void Build()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool standalone = target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows ||
                              target == BuildTarget.StandaloneOSX || target == BuildTarget.StandaloneLinux64;
            if (!standalone)
            {
                EditorUtility.DisplayDialog("Autoplay build",
                    $"The active build target is {target}. Switch to a desktop platform (File → Build Profiles) first.", "OK");
                return;
            }

            string exe = target == BuildTarget.StandaloneOSX ? "AntiqueTradingSimulator.app"
                       : target == BuildTarget.StandaloneLinux64 ? "AntiqueTradingSimulator.x86_64"
                       : "AntiqueTradingSimulator.exe";

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(BuildFolder, exe),
                target = target,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "ATS_DIAGNOSTICS" }
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"Autoplay build: {summary.result} — {Path.GetFullPath(options.locationPathName)} " +
                      $"({summary.totalErrors} errors, {summary.totalTime.TotalSeconds:0}s)");
            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(options.locationPathName);
        }
    }
}
