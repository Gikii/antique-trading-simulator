using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AntiqueTradingSimulator.Autoplay
{
    /// <summary>
    /// Settings of an autoplay run, read from command-line arguments (a build started by
    /// Tools/Autoplay/run_experiments.py) or, in the editor, from the Autoplay window.
    ///
    /// Arguments (case-insensitive):
    ///   -autoplay                      turns autoplay on
    ///   -autoplayStrategy:&lt;name&gt;       idle | buyup | trader (default idle)
    ///   -autoplayWeeks:&lt;n&gt;             stop after week n (default: the whole campaign)
    ///   -autoplayTicksPerFrame:&lt;n&gt;     ticks played per frame (default 28 = one week)
    ///   -autoplayQuit                  quit the game when the run is over
    ///   -autoplayCategory:&lt;type&gt;       buyup: AntiqueType to buy up (default Painting)
    ///   -autoplayFrom:&lt;week&gt;           buyup: first week of buying (default 20)
    ///   -autoplayTo:&lt;week&gt;             buyup: last week of buying (default 45)
    ///   -autoplaySell:&lt;week&gt;           buyup: week the stock is dumped, -1 = keep it (default 70)
    ///   -autoplayGrant:&lt;£&gt;             cash handed to the player when buying starts (default 0)
    ///   -autoplayReserve:&lt;£&gt;           cash the bot never spends, for upkeep (default 2)
    ///   -set:&lt;target&gt;.&lt;member&gt;=&lt;value&gt;  parameter override, may repeat — see AutoplayOverrides
    ///   -atsSeed:&lt;n&gt;                   seed (shared with the diagnostics)
    /// </summary>
    public sealed class AutoplayConfig
    {
        public const string EditorArgsKey = "ATS.Autoplay.Args";
        public const string EditorRunOnceKey = "ATS.Autoplay.RunOnce";

        public bool Active;
        public string Strategy = "idle";
        public int MaxWeeks = -1;
        public int TicksPerFrame = 28;
        public bool QuitWhenDone;

        public string Category = "Painting";
        public int FromWeek = 20;
        public int ToWeek = 45;
        public int SellWeek = 70;
        public float Grant;
        public float Reserve = 2f;

        public bool HasSeed;
        public int Seed;

        /// <summary>Raw "target.member=value" overrides in the order given.</summary>
        public readonly List<string> Overrides = new List<string>();

        /// <summary>Every argument this config was read from, for the run summary.</summary>
        public readonly List<string> Arguments = new List<string>();

        public static AutoplayConfig Read()
        {
            var args = new List<string>();
            try
            {
                args.AddRange(Environment.GetCommandLineArgs());
            }
            catch
            {
                // No command line: nothing to add.
            }

#if UNITY_EDITOR
            // The editor's own command line can't be changed per run, so the Autoplay window leaves
            // its arguments in EditorPrefs and marks one run. Consumed here so a normal Play is unaffected.
            if (UnityEditor.EditorPrefs.GetBool(EditorRunOnceKey, false))
            {
                UnityEditor.EditorPrefs.SetBool(EditorRunOnceKey, false);
                args.Add("-autoplay");
                args.AddRange(Split(UnityEditor.EditorPrefs.GetString(EditorArgsKey, "")));
            }
#endif
            return Parse(args);
        }

        public static AutoplayConfig Parse(IEnumerable<string> args)
        {
            var config = new AutoplayConfig();
            foreach (string raw in args)
            {
                if (string.IsNullOrEmpty(raw) || raw[0] != '-') continue;

                int colon = raw.IndexOf(':');
                string key = (colon >= 0 ? raw.Substring(0, colon) : raw).ToLowerInvariant();
                string value = colon >= 0 ? raw.Substring(colon + 1).Trim('"') : "";

                switch (key)
                {
                    case "-autoplay": config.Active = true; break;
                    case "-autoplaystrategy": config.Strategy = value.ToLowerInvariant(); break;
                    case "-autoplayweeks": config.MaxWeeks = Int(value, config.MaxWeeks); break;
                    case "-autoplayticksperframe": config.TicksPerFrame = Mathf.Max(1, Int(value, config.TicksPerFrame)); break;
                    case "-autoplayquit": config.QuitWhenDone = true; break;
                    case "-autoplaycategory": config.Category = value; break;
                    case "-autoplayfrom": config.FromWeek = Int(value, config.FromWeek); break;
                    case "-autoplayto": config.ToWeek = Int(value, config.ToWeek); break;
                    case "-autoplaysell": config.SellWeek = Int(value, config.SellWeek); break;
                    case "-autoplaygrant": config.Grant = Float(value, config.Grant); break;
                    case "-autoplayreserve": config.Reserve = Float(value, config.Reserve); break;
                    case "-set": if (value.Length > 0) config.Overrides.Add(value); break;
                    case "-atsseed":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                        {
                            config.HasSeed = true;
                            config.Seed = seed;
                        }
                        break;
                    default:
                        continue; // not ours (Unity's own -batchmode, -logFile, the diagnostics' flags, …)
                }

                config.Arguments.Add(raw);
            }

            return config;
        }

        /// <summary>Splits a line of arguments on spaces, keeping "quoted parts" together.</summary>
        public static List<string> Split(string line)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(line)) return parts;

            var current = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0) parts.Add(current.ToString());
            return parts;
        }

        private static int Int(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

        private static float Float(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
    }
}
