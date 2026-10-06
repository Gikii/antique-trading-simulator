using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One ledger entry in the Finances transaction table.</summary>
    public class TransactionRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text amountText;

        public void Set(string date, string type, string description, float amount)
        {
            if (dateText != null) dateText.text = date;
            if (typeText != null) typeText.text = type;
            if (descriptionText != null) descriptionText.text = description;
            if (amountText != null) amountText.text = UIFormat.ColorBySign(UIFormat.SignedMoney(amount), amount);
        }
    }
}
