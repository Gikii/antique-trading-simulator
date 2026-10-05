using AntiqueTradingSimulator.Company;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public enum CompanyTab
    {
        Overview,
        Development,
        Reputation,
        Finances
    }

    /// <summary>
    /// Company view (layout built by Tools > Antique Trading Simulator > Build Company View).
    /// Switches between its sub-tabs; each page is its own component (CompanyOverviewTab, ...)
    /// that refreshes itself while it is active.
    /// </summary>
    public class CompanyView : UIView
    {
        [Tooltip("One button per CompanyTab, in enum order.")]
        [SerializeField] private Button[] tabButtons = new Button[4];

        [Tooltip("One page per CompanyTab, in enum order.")]
        [SerializeField] private GameObject[] tabPages = new GameObject[4];

        [Tooltip("Development page component, so other screens can open a specific upgrade.")]
        [SerializeField] private CompanyDevelopmentTab developmentTab;

        [SerializeField] private Color activeTabColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color inactiveTabColor = Color.white;

        private CompanyTab _currentTab = CompanyTab.Overview;
        private bool _listenersAdded;

        public CompanyTab CurrentTab => _currentTab;

        private void Awake()
        {
            AddListeners();
        }

        private void AddListeners()
        {
            if (_listenersAdded) return;
            _listenersAdded = true;

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                var tab = (CompanyTab)i;
                tabButtons[i].onClick.AddListener(() => ShowTab(tab));
            }
        }

        protected override void OnShown()
        {
            ShowTab(_currentTab);
        }

        public void ShowTab(CompanyTab tab)
        {
            _currentTab = tab;

            for (int i = 0; i < tabPages.Length; i++)
                if (tabPages[i] != null)
                    tabPages[i].SetActive(i == (int)tab);

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                var image = tabButtons[i].targetGraphic as Image;
                if (image != null)
                    image.color = i == (int)tab ? activeTabColor : inactiveTabColor;
            }
        }

        /// <summary>
        /// Switches to Development with the given upgrade selected. Call after the
        /// ViewManager has shown the Company view.
        /// </summary>
        public void OpenUpgrade(CompanyUpgradeType type)
        {
            ShowTab(CompanyTab.Development);
            if (developmentTab != null)
                developmentTab.Select(type);
        }

        // Parameterless wrappers for Button.onClick in the Inspector.
        public void ShowOverview() => ShowTab(CompanyTab.Overview);
        public void ShowDevelopment() => ShowTab(CompanyTab.Development);
        public void ShowReputation() => ShowTab(CompanyTab.Reputation);
        public void ShowFinances() => ShowTab(CompanyTab.Finances);
    }
}
