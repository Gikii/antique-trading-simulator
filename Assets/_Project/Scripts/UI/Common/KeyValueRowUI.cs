using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A "label ........ value" line, e.g. "Capacity      12 items".</summary>
    public class KeyValueRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;

        public void Set(string label, string value)
        {
            if (labelText != null) labelText.text = label;
            if (valueText != null) valueText.text = value;
        }
    }
}
