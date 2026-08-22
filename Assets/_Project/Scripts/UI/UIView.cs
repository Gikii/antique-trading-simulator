using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Base class for any full-panel view switched by the ViewManager
    /// (Market, Inventory, Auctions, ...). Subclasses can override
    /// OnShown/OnHidden to refresh their content when they become visible.
    /// </summary>
    public abstract class UIView : MonoBehaviour
    {
        public void Show()
        {
            gameObject.SetActive(true);
            OnShown();
        }

        public void Hide()
        {
            OnHidden();
            gameObject.SetActive(false);
        }

        protected virtual void OnShown() { }
        protected virtual void OnHidden() { }
    }
}