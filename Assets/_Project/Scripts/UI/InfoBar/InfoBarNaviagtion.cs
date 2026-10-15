using AntiqueTradingSimulator.Company;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    public static class InfoBarNavigation
    {
        public static void OpenInventory() => ShowView(ViewType.Inventory);

        public static void OpenNews()
        {
            var view = OpenNewsEvents();
            if (view != null)
                view.ShowNewsMode();
        }

        public static void OpenEvents()
        {
            var view = OpenNewsEvents();
            if (view != null)
                view.ShowActiveEventsMode();
        }

        public static void OpenWarehouseUpgrades()
        {
            var companyView = FindView<CompanyView>();
            if (companyView == null || !ShowView(ViewType.Company))
            {
                Debug.LogWarning("InfoBarNavigation: no companyView in the scene.");
                return;
            }

            companyView.OpenUpgrade(CompanyUpgradeType.Warehouse);
        }

        private static NewsEventsView OpenNewsEvents()
        {
            var view = FindView<NewsEventsView>();
            if (view == null)
            {
                Debug.LogWarning("InfoBarNavigation: no NewsEventsView in the scene.");
                return null;
            }

            return ShowView(ViewType.NewsEvents) ? view : null;
        }

        private static bool ShowView(ViewType type)
        {
            var viewManager = Object.FindFirstObjectByType<ViewManager>();
            if (viewManager == null)
            {
                Debug.LogWarning("InfoBarNavigation: no ViewManager in the scene.");
                return false;
            }

            viewManager.ShowView(type);
            return true;
        }

        private static T FindView<T>() where T : UIView
            => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }
}