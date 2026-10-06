using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One category line under Income or Expenses: name, amount and share of the total.</summary>
    public class FinanceCategoryRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text shareText;

        public void Set(string categoryName, string amount, string share)
        {
            if (nameText != null) nameText.text = categoryName;
            if (amountText != null) amountText.text = amount;
            if (shareText != null) shareText.text = share;
        }
    }
}
