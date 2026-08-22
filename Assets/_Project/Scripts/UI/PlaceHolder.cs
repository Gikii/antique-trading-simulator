using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Temporary stand-in for a view that isn't built yet — just shows
    /// its name so navigation is fully wired end-to-end. Replace with a
    /// real view (subclassed from UIView, with its own logic) as each
    /// system gets implemented -> (Inventory/Auctions/Contracts/Warehouse/Company/News/Events)
    /// </summary>
    public class PlaceholderView : UIView
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private string viewName = "Coming soon";

        protected override void OnShown()
        {
            if (label != null)
                label.text = viewName;
        }
    }
}