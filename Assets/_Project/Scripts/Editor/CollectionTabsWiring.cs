using AntiqueTradingSimulator.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Connects the Collection view's tab buttons (InventoryHeader/TabsRow) to the InventoryView
    /// fields that make them work — the fields were empty, so the tabs neither filtered the list
    /// nor showed which one is active:
    ///   CollectionTab → collectionTabButton (all antiques)
    ///   TransportTab  → transportTabButton  (in transit)
    ///   RenovationTab → renovationTabButton (in restoration)
    ///   AuctionTab    → forSaleTabButton    (listed for sale)
    /// HistoryTab has no CollectionTab yet and stays unconnected. Safe to run more than once.
    /// </summary>
    public static class CollectionTabsWiring
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Collection/Wire Tabs";
        private const string TabsRowPath = "InventoryHeader/TabsRow";

        private static readonly (string button, string field)[] Tabs =
        {
            ("CollectionTab", "collectionTabButton"),
            ("TransportTab", "transportTabButton"),
            ("RenovationTab", "renovationTabButton"),
            ("AuctionTab", "forSaleTabButton"),
        };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var view = Object.FindFirstObjectByType<InventoryView>(FindObjectsInactive.Include);
            if (view == null)
            {
                Debug.LogError("[CollectionTabsWiring] No InventoryView in the open scene — open Main.unity.");
                return;
            }

            Transform row = view.transform.Find(TabsRowPath);
            if (row == null)
            {
                Debug.LogError($"[CollectionTabsWiring] '{TabsRowPath}' not found under {view.name}.", view);
                return;
            }

            var so = new SerializedObject(view);
            int wired = 0;
            foreach (var (buttonName, fieldName) in Tabs)
            {
                var prop = so.FindProperty(fieldName);
                var child = row.Find(buttonName);
                var button = child != null ? child.GetComponent<Button>() : null;

                if (prop == null)
                {
                    Debug.LogWarning($"[CollectionTabsWiring] InventoryView has no field '{fieldName}'.", view);
                    continue;
                }
                if (button == null)
                {
                    Debug.LogWarning($"[CollectionTabsWiring] '{TabsRowPath}/{buttonName}' has no Button.", view);
                    continue;
                }

                prop.objectReferenceValue = button;
                wired++;
            }

            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            Debug.Log($"[CollectionTabsWiring] Wired {wired} tab(s). Save the scene (Ctrl+S).", view);
        }
    }
}
