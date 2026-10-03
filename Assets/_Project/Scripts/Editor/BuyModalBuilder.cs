using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// One-click setup for the transport UI (UI patch A). Builds BuyModal in the open scene
    /// by duplicating SellNowModal and reworking the copy (so it keeps the same look),
    /// wires every BuyModalUI / TransportOptionUI field, and adds the "Ships from" lines to
    /// the market details panel and the ListingCard prefab.
    ///
    /// Menu: Tools → Antique Trading Simulator → Build Buy Modal.
    /// Scene changes are undoable (Ctrl+Z); the prefab change is saved directly.
    /// Safe to delete this file once the UI is built.
    /// </summary>
    public static class BuyModalBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Build Buy Modal";
        private const string ListingCardPath = "Assets/_Project/Prefabs/UI/ListingCard.prefab";
        private const string ModalName = "BuyModal";

        private const float MinWindowHeight = 510f;
        private const float MaxWindowHeight = 900f;
        private const float TransportRowHeight = 104f;

        [MenuItem(MenuPath, true)]
        private static bool Validate() => !EditorApplication.isPlaying;

        [MenuItem(MenuPath)]
        public static void Build()
        {
            Undo.SetCurrentGroupName("Build Buy Modal");
            int undoGroup = Undo.GetCurrentGroup();

            BuyModalUI modal = BuildModal();
            if (modal == null)
                return;

            AddShippingLineToDetailsPanel(modal);
            AddShippingLineToListingCard();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(modal.gameObject.scene);
            Selection.activeGameObject = modal.gameObject;
            EditorGUIUtility.PingObject(modal.gameObject);

            Debug.Log("BuyModalBuilder: done. Review BuyModal in the Hierarchy, then save the scene (Ctrl+S).", modal);
        }

        // ------------------------------------------------------------------
        // Modal
        // ------------------------------------------------------------------

        private static BuyModalUI BuildModal()
        {
            var sellModal = Object.FindFirstObjectByType<SellNowModalUI>(FindObjectsInactive.Include);
            if (sellModal == null)
            {
                EditorUtility.DisplayDialog("Build Buy Modal",
                    "No SellNowModalUI found in the open scene. Open the Main scene and try again.", "OK");
                return null;
            }

            Transform parent = sellModal.transform.parent;

            Transform existing = parent != null ? parent.Find(ModalName) : null;
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Build Buy Modal",
                        $"'{ModalName}' already exists under '{parent.name}'. Replace it with a freshly built one?",
                        "Replace", "Cancel"))
                    return null;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            // --- Duplicate SellNowModal ---------------------------------------------------
            GameObject root = Object.Instantiate(sellModal.gameObject, parent);
            root.name = ModalName;
            root.transform.SetSiblingIndex(sellModal.transform.GetSiblingIndex() + 1);
            root.SetActive(false); // BuyModalUI.Open activates it
            Undo.RegisterCreatedObjectUndo(root, "Create BuyModal");

            Object.DestroyImmediate(root.GetComponent<SellNowModalUI>());
            var buyModal = root.AddComponent<BuyModalUI>();

            Transform window = root.transform.Find("Window");
            Transform body = Find(root.transform, "Window/Body");
            if (window == null || body == null)
            {
                Debug.LogError("BuyModalBuilder: SellNowModal no longer has Window/Body — the layout changed, build the modal by hand.");
                Undo.DestroyObjectImmediate(root);
                return null;
            }

            // --- Header -----------------------------------------------------------------
            SetText(root.transform, "Window/Header/HeaderInfo/TitleText", "BUY ANTIQUE?");
            TMP_Text shipsFrom = SetText(root.transform, "Window/Header/HeaderInfo/SubtitleText", "Ships from: —");
            Button closeButton = FindComponent<Button>(root.transform, "Window/Header/CloseButton");

            // --- Body: texts reused from Sell Now ---------------------------------------
            TMP_Text title = SetText(body, "QuestionText", "Buy <b>Antique</b>?");
            TMP_Text itemInfo = SetText(body, "InfoText", "Condition · century · country");

            Transform priceRow = body.Find("MarketValueRow");
            Transform totalRow = body.Find("ReceiveRow");
            TMP_Text warning = SetText(body, "ProfitText", "");

            if (priceRow == null || totalRow == null)
            {
                Debug.LogError("BuyModalBuilder: MarketValueRow/ReceiveRow not found in SellNowModal — build the modal by hand.");
                Undo.DestroyObjectImmediate(root);
                return null;
            }

            TMP_Text price = SetupRow(priceRow, "PriceRow", "Price:");
            TMP_Text total = SetupRow(totalRow, "TotalRow", "<b>Total:</b>");

            // Order: Price, Shipping, ─ divider ─, Total, Arrives, Cash after, Warehouse
            TMP_Text shipping = DuplicateRow(priceRow, "ShippingRow", "Shipping:", priceRow.GetSiblingIndex() + 1);
            int afterTotal = totalRow.GetSiblingIndex() + 1;
            TMP_Text arrival = DuplicateRow(priceRow, "ArrivalRow", "Arrives:", afterTotal++);
            TMP_Text cashAfter = DuplicateRow(priceRow, "CashAfterRow", "Cash after purchase:", afterTotal++);
            TMP_Text warehouse = DuplicateRow(priceRow, "WarehouseRow", "Warehouse:", afterTotal);

            // --- Transport options ------------------------------------------------------
            TMP_Text textTemplate = priceRow.Find("Label").GetComponent<TMP_Text>();
            Transform options = CreateTransportRow(body, itemInfo.transform.GetSiblingIndex() + 1);

            var optionUIs = new TransportOptionUI[3];
            optionUIs[0] = CreateTransportTile(options, textTemplate, TransportOption.Economy);
            optionUIs[1] = CreateTransportTile(options, textTemplate, TransportOption.Standard);
            optionUIs[2] = CreateTransportTile(options, textTemplate, TransportOption.Express);

            // --- Footer -----------------------------------------------------------------
            Button cancelButton = FindComponent<Button>(root.transform, "Window/Footer/CancelButton");
            Button confirmButton = FindComponent<Button>(root.transform, "Window/Footer/ConfirmButton");
            SetText(root.transform, "Window/Footer/ConfirmButton/Text", "Buy");

            // --- Wire BuyModalUI --------------------------------------------------------
            var so = new SerializedObject(buyModal);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("itemInfoText").objectReferenceValue = itemInfo;
            so.FindProperty("priceText").objectReferenceValue = price;
            so.FindProperty("shipsFromText").objectReferenceValue = shipsFrom;
            so.FindProperty("shippingCostText").objectReferenceValue = shipping;
            so.FindProperty("totalCostText").objectReferenceValue = total;
            so.FindProperty("arrivalText").objectReferenceValue = arrival;
            so.FindProperty("cashAfterText").objectReferenceValue = cashAfter;
            so.FindProperty("warehouseText").objectReferenceValue = warehouse;
            so.FindProperty("warningText").objectReferenceValue = warning;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("cancelButton").objectReferenceValue = cancelButton;
            so.FindProperty("confirmButton").objectReferenceValue = confirmButton;

            SerializedProperty list = so.FindProperty("transportOptions");
            list.arraySize = optionUIs.Length;
            for (int i = 0; i < optionUIs.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = optionUIs[i];

            so.ApplyModifiedPropertiesWithoutUndo(); // the whole object is already one undo step

            FitWindowHeight(root, window as RectTransform);
            return buyModal;
        }

        private static TMP_Text SetupRow(Transform row, string name, string label)
        {
            row.name = name;
            SetText(row, "Label", label);
            return SetText(row, "Value", "—");
        }

        private static TMP_Text DuplicateRow(Transform template, string name, string label, int siblingIndex)
        {
            GameObject copy = Object.Instantiate(template.gameObject, template.parent);
            copy.transform.SetSiblingIndex(siblingIndex);
            return SetupRow(copy.transform, name, label);
        }

        private static Transform CreateTransportRow(Transform body, int siblingIndex)
        {
            var row = new GameObject("TransportOptions", typeof(RectTransform));
            row.transform.SetParent(body, false);
            row.transform.SetSiblingIndex(siblingIndex);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var element = row.AddComponent<LayoutElement>();
            element.minHeight = TransportRowHeight;
            element.preferredHeight = TransportRowHeight;

            return row.transform;
        }

        private static TransportOptionUI CreateTransportTile(Transform parent, TMP_Text textTemplate, TransportOption option)
        {
            var tile = new GameObject($"Option_{option}", typeof(RectTransform));
            tile.transform.SetParent(parent, false);

            var image = tile.AddComponent<Image>();
            image.color = new Color(0.27f, 0.27f, 0.27f, 1f);

            var button = tile.AddComponent<Button>();
            button.targetGraphic = image;

            var layout = tile.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = tile.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;

            TMP_Text name = CreateTileText(tile.transform, textTemplate, "NameText", option.ToDisplayString(), 18f, FontStyles.Bold);
            TMP_Text cost = CreateTileText(tile.transform, textTemplate, "CostText", "— €", 18f, FontStyles.Normal);
            TMP_Text duration = CreateTileText(tile.transform, textTemplate, "DurationText", "— days", 16f, FontStyles.Normal);
            TMP_Text risk = CreateTileText(tile.transform, textTemplate, "RiskText", "—", 14f, FontStyles.Italic);

            var optionUI = tile.AddComponent<TransportOptionUI>();
            var so = new SerializedObject(optionUI);
            so.FindProperty("option").enumValueIndex = (int)option;
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("nameText").objectReferenceValue = name;
            so.FindProperty("costText").objectReferenceValue = cost;
            so.FindProperty("durationText").objectReferenceValue = duration;
            so.FindProperty("riskText").objectReferenceValue = risk;
            so.FindProperty("background").objectReferenceValue = image;
            so.FindProperty("normalColor").colorValue = image.color;
            so.ApplyModifiedPropertiesWithoutUndo();

            return optionUI;
        }

        private static TMP_Text CreateTileText(Transform parent, TMP_Text template, string name, string text, float size, FontStyles style)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent);
            go.name = name;

            // The row label's LayoutElement (flexible width) doesn't belong in a tile.
            var element = go.GetComponent<LayoutElement>();
            if (element != null)
                Object.DestroyImmediate(element);

            var tmp = go.GetComponent<TMP_Text>();
            tmp.text = text;
            tmp.enableAutoSizing = false;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            return tmp;
        }

        /// <summary>The Sell Now window has a fixed height; grow it to fit the extra rows.</summary>
        private static void FitWindowHeight(GameObject root, RectTransform window)
        {
            if (window == null) return;

            // Layout only computes for active objects — activate briefly to measure.
            root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(window);
            float preferred = LayoutUtility.GetPreferredHeight(window);
            root.SetActive(false);

            float height = Mathf.Clamp(Mathf.Ceil(preferred), MinWindowHeight, MaxWindowHeight);
            window.sizeDelta = new Vector2(window.sizeDelta.x, height);
        }

        // ------------------------------------------------------------------
        // Market details panel: "Ships from International · 5 days, ~120 € (Standard)"
        // ------------------------------------------------------------------

        private static void AddShippingLineToDetailsPanel(BuyModalUI modal)
        {
            var details = Object.FindFirstObjectByType<AntiqueDetailsUI>(FindObjectsInactive.Include);
            if (details == null)
            {
                Debug.LogWarning("BuyModalBuilder: no AntiqueDetailsUI in the scene — skipped the details shipping line.");
                return;
            }

            var so = new SerializedObject(details);
            so.FindProperty("buyModal").objectReferenceValue = modal;

            SerializedProperty shippingProp = so.FindProperty("shippingText");
            if (shippingProp.objectReferenceValue == null)
            {
                Transform style = details.transform.Find("OverviewSection/InfoColumn/OriginText");
                Transform priceText = details.transform.Find("ActionSection/PriceText");

                if (style != null && priceText != null)
                {
                    GameObject line = Object.Instantiate(style.gameObject, priceText.parent);
                    line.name = "ShippingText";
                    line.transform.SetSiblingIndex(priceText.GetSiblingIndex() + 1);
                    Undo.RegisterCreatedObjectUndo(line, "Add shipping line");

                    var tmp = line.GetComponent<TMP_Text>();
                    tmp.text = "Ships from —";
                    shippingProp.objectReferenceValue = tmp;

                    GrowFixedHeight(priceText.parent, tmp);
                }
                else
                {
                    Debug.LogWarning("BuyModalBuilder: AntiqueDetails layout changed (OriginText / ActionSection/PriceText not found) — add Shipping Text by hand.");
                }
            }

            so.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------
        // ListingCard prefab: "Ships from: International"
        // ------------------------------------------------------------------

        private static void AddShippingLineToListingCard()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ListingCardPath) == null)
            {
                Debug.LogWarning($"BuyModalBuilder: {ListingCardPath} not found — skipped the card shipping line.");
                return;
            }

            GameObject card = PrefabUtility.LoadPrefabContents(ListingCardPath);
            try
            {
                var listingUI = card.GetComponent<MarketListingUI>();
                if (listingUI == null)
                {
                    Debug.LogWarning("BuyModalBuilder: ListingCard has no MarketListingUI — skipped.");
                    return;
                }

                var so = new SerializedObject(listingUI);
                SerializedProperty shippingProp = so.FindProperty("shippingText");
                if (shippingProp.objectReferenceValue != null)
                    return; // already set up

                Transform seller = card.transform.Find("Layout/MainContent/InfoColumn/SellerText");
                if (seller == null)
                {
                    Debug.LogWarning("BuyModalBuilder: ListingCard layout changed (SellerText not found) — add Shipping Text by hand.");
                    return;
                }

                GameObject line = Object.Instantiate(seller.gameObject, seller.parent);
                line.name = "ShippingText";
                line.transform.SetSiblingIndex(seller.GetSiblingIndex() + 1);

                var tmp = line.GetComponent<TMP_Text>();
                tmp.text = "Ships from: —";
                shippingProp.objectReferenceValue = tmp;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(card, ListingCardPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(card);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>If the container has a fixed preferred height, make room for the new line.</summary>
        private static void GrowFixedHeight(Transform container, TMP_Text addedLine)
        {
            var element = container.GetComponent<LayoutElement>();
            if (element == null || element.preferredHeight <= 0f)
                return;

            Undo.RecordObject(element, "Grow section");
            var layout = container.GetComponent<VerticalLayoutGroup>();
            float spacing = layout != null ? layout.spacing : 0f;
            element.preferredHeight += Mathf.Max(addedLine.preferredHeight, 20f) + spacing;
        }

        private static Transform Find(Transform root, string path) => root.Find(path);

        private static T FindComponent<T>(Transform root, string path) where T : Component
        {
            Transform t = root.Find(path);
            if (t == null)
            {
                Debug.LogWarning($"BuyModalBuilder: '{path}' not found under {root.name}.");
                return null;
            }
            return t.GetComponent<T>();
        }

        private static TMP_Text SetText(Transform root, string path, string text)
        {
            var tmp = FindComponent<TMP_Text>(root, path);
            if (tmp != null)
                tmp.text = text;
            return tmp;
        }
    }
}
