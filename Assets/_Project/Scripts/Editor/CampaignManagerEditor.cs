using AntiqueTradingSimulator.Company;
using UnityEditor;
using UnityEngine;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// CampaignManager's Inspector: its fields, the campaign's state, and in Play mode a button
    /// that ends the campaign at once — to test the end screen without playing every day.
    /// </summary>
    [CustomEditor(typeof(CampaignManager))]
    public class CampaignManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var campaign = (CampaignManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Campaign", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to see the campaign's state and to end it from here.", MessageType.Info);
                return;
            }

            if (!campaign.IsFinished)
            {
                EditorGUILayout.LabelField("State", $"Running · ends after day {campaign.CampaignLength}");

                if (GUILayout.Button("Finish campaign now", GUILayout.Height(28f)))
                    campaign.Finish();
                return;
            }

            EditorGUILayout.LabelField("State", $"Finished on day {campaign.FinishedOnDay}");
            foreach (var standing in campaign.FinalRanking)
                EditorGUILayout.LabelField($"{standing.Place}. {standing.Name}{(standing.IsPlayer ? " (player)" : "")}",
                                           $"{standing.Score:0}");

            var screen = FindFirstObjectByType<AntiqueTradingSimulator.UI.EndScreenUI>(FindObjectsInactive.Include);
            using (new EditorGUI.DisabledScope(screen == null))
            {
                if (GUILayout.Button(screen != null ? "Show results again" : "Show results again (no EndScreen in the scene)", GUILayout.Height(28f)))
                    screen.Show();
            }
        }

        // Keeps the state and ranking current while the game runs.
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
