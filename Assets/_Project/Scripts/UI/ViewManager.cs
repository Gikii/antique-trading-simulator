using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public enum ViewType
    {
        Market,
        Inventory,
        Auctions,
        Contracts,
        Warehouse,
        Company,
        NewsEvents
    }

    /// <summary>
    /// Central controller for the single-scene, multi-view UI. Exactly one
    /// registered view is active at a time. MainNavigation buttons call
    /// ShowView with the ViewType they represent.
    /// </summary>
    public class ViewManager : MonoBehaviour
    {
        [Serializable]
        private struct ViewEntry
        {
            public ViewType type;
            public UIView view;
            [Tooltip("Optional MainNavigation button of this view — highlighted while the view is shown.")]
            public Button navButton;
        }

        [SerializeField] private List<ViewEntry> views;
        [SerializeField] private ViewType defaultView = ViewType.Market;

        private readonly Dictionary<ViewType, UIView> _viewsByType = new Dictionary<ViewType, UIView>();

        void Awake()
        {
            foreach (var entry in views)
            {
                if (entry.view == null)
                {
                    Debug.LogWarning($"ViewManager: no UIView assigned for {entry.type}");
                    continue;
                }
                _viewsByType[entry.type] = entry.view;
            }
        }

        void Start()
        {
            ShowView(defaultView);
        }

        public void ShowView(ViewType type)
        {
            foreach (var kvp in _viewsByType)
                kvp.Value.Hide();

            if (_viewsByType.TryGetValue(type, out var view))
                view.Show();
            else
                Debug.LogWarning($"ViewManager: no view registered for {type}");

            HighlightNavigation(type);
        }

        /// <summary>Name of the small second line under a navigation button's title (not recoloured).</summary>
        public const string NavSubtitleName = "Subtitle";

        /// <summary>
        /// Swaps the navigation buttons' frame between the theme's idle and active sprites;
        /// the active button's title turns gold.
        /// </summary>
        private void HighlightNavigation(ViewType active)
        {
            var theme = UITheme.Load();
            if (theme == null || theme.NavItem == null || theme.NavActive == null)
                return; // no theme yet — greybox buttons stay as they are

            foreach (var entry in views)
            {
                if (entry.navButton == null || !(entry.navButton.targetGraphic is Image image))
                    continue;

                bool isActive = entry.type == active;
                image.sprite = isActive ? theme.NavActive : theme.NavItem;
                image.type = Image.Type.Sliced;

                foreach (var label in entry.navButton.GetComponentsInChildren<TMP_Text>(true))
                    if (label.name != NavSubtitleName)
                        label.color = isActive ? theme.Accent : theme.TextPrimary;
            }
        }

        // Parameterless wrappers so Unity's Button.onClick inspector can bind to them
        // directly — UnityEvent doesn't support custom enum parameters in its inspector.
        public void ShowMarket() => ShowView(ViewType.Market);
        public void ShowInventory() => ShowView(ViewType.Inventory);
        public void ShowAuctions() => ShowView(ViewType.Auctions);
        public void ShowContracts() => ShowView(ViewType.Contracts);
        public void ShowWarehouse() => ShowView(ViewType.Warehouse);
        public void ShowCompany() => ShowView(ViewType.Company);
        public void ShowNewsEvents() => ShowView(ViewType.NewsEvents);

    }
}