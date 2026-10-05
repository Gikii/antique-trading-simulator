using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Company → Reputation: reputation and credibility side by side (value with trend and
    /// chart, tier ladder, recent changes). "Show all" on either list switches the page into
    /// history mode — the upper rows hide and both lists show their full, paged history.
    /// </summary>
    public class CompanyReputationTab : MonoBehaviour
    {
        [Header("Dependencies (found automatically if empty)")]
        [SerializeField] private PlayerTrader playerTrader;

        [SerializeField] private ReputationColumnUI reputationColumn;
        [SerializeField] private ReputationColumnUI credibilityColumn;

        [Tooltip("Rows hidden while the full history is shown (value cards, level panels).")]
        [SerializeField] private GameObject[] hideWhenExpanded = new GameObject[0];

        private CompanyManager _company;
        private bool _expanded;

        private void OnEnable()
        {
            Bind();
            Subscribe(reputationColumn, true);
            Subscribe(credibilityColumn, true);
            Refresh();
        }

        private void OnDisable()
        {
            Subscribe(reputationColumn, false);
            Subscribe(credibilityColumn, false);
            if (_company != null) _company.OnCompanyChanged -= Refresh;
            _company = null;
        }

        private void Bind()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (_company != null || playerTrader == null || playerTrader.Company == null) return;

            _company = playerTrader.Company;
            _company.OnCompanyChanged += Refresh;
        }

        private void Subscribe(ReputationColumnUI column, bool subscribe)
        {
            var list = column != null ? column.ChangeList : null;
            if (list == null) return;

            if (subscribe) list.OnToggleClicked += ToggleExpanded;
            else list.OnToggleClicked -= ToggleExpanded;
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            Refresh();
        }

        public void Refresh()
        {
            if (_company == null) Bind();

            foreach (var row in hideWhenExpanded)
                if (row != null) row.SetActive(!_expanded);

            if (_company == null) return;

            if (reputationColumn != null) reputationColumn.Refresh(_company, _expanded);
            if (credibilityColumn != null) credibilityColumn.Refresh(_company, _expanded);
        }
    }
}
