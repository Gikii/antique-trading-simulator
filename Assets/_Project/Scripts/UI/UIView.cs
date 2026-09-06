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
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            OnShown();
        }

        private void OnDisable()
        {
            OnHidden();
        }

        protected virtual void OnShown() { }

        protected virtual void OnHidden() { }
    }
}