using System;
using System.Collections.Generic;
using UnityEngine;

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
        News,
        Events
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
        }

        // Parameterless wrappers so Unity's Button.onClick inspector can bind to them
        // directly — UnityEvent doesn't support custom enum parameters in its inspector.
        public void ShowMarket() => ShowView(ViewType.Market);
        public void ShowInventory() => ShowView(ViewType.Inventory);
        public void ShowAuctions() => ShowView(ViewType.Auctions);
        public void ShowContracts() => ShowView(ViewType.Contracts);
        public void ShowWarehouse() => ShowView(ViewType.Warehouse);
        public void ShowCompany() => ShowView(ViewType.Company);
        public void ShowNews() => ShowView(ViewType.News);
        public void ShowEvents() => ShowView(ViewType.Events);

    }
}