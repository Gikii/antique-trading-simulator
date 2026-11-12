using AntiqueTradingSimulator.Market;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Right-hand panel of the Auctions view: the selected lot's picture, name, attributes,
    /// provenance and a "label …… value" list of facts. The view decides which facts to show
    /// (catalog vs. results), this panel only lays them out.
    /// </summary>
    public class AuctionLotDetailsUI : MonoBehaviour
    {
        [SerializeField] private GameObject emptyState;
        [SerializeField] private GameObject content;

        [SerializeField] private Image image;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private TMP_Text provenanceText;

        [Tooltip("Left column of the facts list (labels, one per line).")]
        [SerializeField] private TMP_Text factLabelsText;
        [Tooltip("Right column of the facts list (values, one per line, right-aligned).")]
        [SerializeField] private TMP_Text factValuesText;

        [SerializeField] private TMP_Text noteText;

        public void ShowEmpty()
        {
            if (emptyState != null) emptyState.SetActive(true);
            if (content != null) content.SetActive(false);
        }

        public void Show(Antique antique, string title, string info, string provenance,
            IReadOnlyList<(string label, string value)> facts, string note)
        {
            if (emptyState != null) emptyState.SetActive(false);
            if (content != null) content.SetActive(true);

            if (image != null) image.ShowAntique(antique);
            Set(nameText, title);
            Set(infoText, info);

            if (provenanceText != null)
            {
                provenanceText.gameObject.SetActive(!string.IsNullOrEmpty(provenance));
                provenanceText.text = string.IsNullOrEmpty(provenance) ? "" : $"<i>{provenance}</i>";
            }

            var labels = new StringBuilder();
            var values = new StringBuilder();
            if (facts != null)
            {
                for (int i = 0; i < facts.Count; i++)
                {
                    if (i > 0) { labels.Append('\n'); values.Append('\n'); }
                    labels.Append(facts[i].label);
                    values.Append(facts[i].value);
                }
            }

            Set(factLabelsText, labels.ToString());
            Set(factValuesText, values.ToString());

            if (noteText != null)
            {
                noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
                noteText.text = note ?? "";
            }
        }

        private static void Set(TMP_Text target, string value)
        {
            if (target != null) target.text = value ?? "";
        }
    }
}
