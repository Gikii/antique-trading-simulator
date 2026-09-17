using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    public class UpcomingEventListItemUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;

        public void Setup(string eventName)
        {
            if (nameText != null)
                nameText.text = eventName;
        }
    }
}
