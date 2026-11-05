using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Market;
using UnityEditor;
using UnityEngine;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Inspector for the dependency network asset: shows the links the rules expand into and
    /// warns about the usual authoring slips.
    /// </summary>
    [CustomEditor(typeof(DependencyGraphSettings))]
    public class DependencyGraphSettingsEditor : Editor
    {
        private string _report;
        private bool _showEdges;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Graph check", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each rule below is expanded into one directed link per direction. Run the check after " +
                "editing the rules — it reads the antique definitions from Resources/Antiques.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check graph"))
                    _report = BuildReport((DependencyGraphSettings)target, out _showEdges);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_report)))
                {
                    if (GUILayout.Button("Clear"))
                        _report = null;
                }
            }

            if (!string.IsNullOrEmpty(_report))
                EditorGUILayout.HelpBox(_report, _report.Contains("WARNING") ? MessageType.Warning : MessageType.Info);
        }

        private static string BuildReport(DependencyGraphSettings settings, out bool hasEdges)
        {
            hasEdges = false;
            if (settings == null) return "No asset selected.";

            // The caches are editor-wide, and the antique assets may have changed since the last
            // check, so start from a clean read.
            AntiqueDatabase.ClearCache();
            MarketFacets.ClearCache();

            var graph = DependencyGraph.Build(settings);
            var warnings = new List<string>();
            var builder = new StringBuilder();

            builder.AppendLine($"{graph.EdgeCount} directed link(s) between categories, {graph.SelfLinkCount} self-link(s).");

            // Hand-written rules: the mistakes worth catching before the game runs.
            if (settings.Rules != null)
            {
                for (int i = 0; i < settings.Rules.Count; i++)
                {
                    var rule = settings.Rules[i];
                    if (rule == null || !rule.Enabled) continue;

                    if (rule.From == rule.To)
                        warnings.Add($"rule {i} ({rule}) links a category to itself — use an AutoRule with SameValueOnly for cohesion inside one category.");

                    if (Mathf.Approximately(rule.Strength, 0f))
                        warnings.Add($"rule {i} ({rule}) has Strength 0 and is dropped when the graph is built.");

                    if (MarketFacets.MemberCount(rule.From) == 0)
                        warnings.Add($"rule {i}: no antique definition belongs to {MarketFacets.DisplayName(rule.From)}, so nothing will ever trigger it.");

                    if (MarketFacets.MemberCount(rule.To) == 0)
                        warnings.Add($"rule {i}: no antique definition belongs to {MarketFacets.DisplayName(rule.To)}, so the link has no effect yet.");
                }
            }

            // Several rules can point at the same pair; their strengths add up and are clamped.
            foreach (var edge in graph.AllEdges())
            {
                hasEdges = true;
                if (Mathf.Abs(edge.Strength) >= 0.999f)
                    warnings.Add($"{edge} sits at the ±1 clamp — rules are probably stacking on this pair.");
            }

            if (hasEdges)
            {
                builder.AppendLine();
                foreach (var edge in graph.AllEdges())
                    builder.AppendLine(edge.ToString());
            }

            if (warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"WARNING ({warnings.Count}):");
                foreach (string warning in warnings)
                    builder.AppendLine("• " + warning);
            }

            return builder.ToString();
        }
    }
}
