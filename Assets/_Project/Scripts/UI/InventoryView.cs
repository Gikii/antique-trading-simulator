using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public class InventoryView : UIView
    {
        [Header("Player")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Listings")]
        [SerializeField] private RectTransform listingsContainer;
        [SerializeField] private GameObject listingRowPrefab;

        [Header("Collection Header")]
        [SerializeField] private TMP_Text collectionTitleText;
        [SerializeField] private TMP_Text collectionValueText;

        [Header("Right Content")]
        [SerializeField] private GameObject collectionSummary;
        [SerializeField] private InventoryDetailsUI inventoryDetailsUI;

        [Header("Pagination - Navigation")]
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Header("Pagination - Page Buttons")]
        [SerializeField] private Button firstPageButton;
        [SerializeField] private TMP_Text firstPageText;

        [SerializeField] private TMP_Text leftEllipsis;

        [SerializeField] private Button previousPageNumberButton;
        [SerializeField] private TMP_Text previousPageNumberText;

        [SerializeField] private Button currentPageButton;
        [SerializeField] private TMP_Text currentPageText;

        [SerializeField] private Button nextPageNumberButton;
        [SerializeField] private TMP_Text nextPageNumberText;

        [SerializeField] private TMP_Text rightEllipsis;

        [SerializeField] private Button lastPageButton;
        [SerializeField] private TMP_Text lastPageText;

        private const int ItemsPerPage = 7;

        private readonly List<Antique> _collection = new();
        private readonly List<InventoryListItemUI> _spawnedRows = new();

        private int _currentPage;
        private bool _subscribed;

        private void Awake()
        {
            if (playerTrader == null)
                playerTrader = FindFirstObjectByType<PlayerTrader>();

            SetupPaginationButtons();

            if (inventoryDetailsUI != null)
                inventoryDetailsUI.Setup(this);

            ShowCollectionSummary();
        }

        protected override void OnShown()
        {
            SubscribeToInventory();
            RefreshCollection();
        }

        private void OnDestroy()
        {
            UnsubscribeFromInventory();
        }

        private void SubscribeToInventory()
        {
            if (_subscribed)
                return;

            if (playerTrader == null || playerTrader.Inventory == null)
                return;

            playerTrader.Inventory.OnHoldingChanged += HandleHoldingChanged;
            _subscribed = true;
        }

        private void UnsubscribeFromInventory()
        {
            if (!_subscribed)
                return;

            if (playerTrader != null && playerTrader.Inventory != null)
                playerTrader.Inventory.OnHoldingChanged -= HandleHoldingChanged;

            _subscribed = false;
        }

        private void HandleHoldingChanged(string listingId, Antique antique)
        {
            RefreshCollection();
        }

        public void RefreshCollection()
        {
            _collection.Clear();

            if (playerTrader != null && playerTrader.Inventory != null)
            {
                _collection.AddRange(
                    playerTrader.Inventory.Holdings.Values
                        .Where(antique => antique != null));
            }

            RefreshHeader();
            RefreshListings();
            RefreshPagination();
        }

        private void RefreshHeader()
        {
            if (collectionTitleText != null)
            {
                collectionTitleText.text =
                    $"MOJA KOLEKCJA ({_collection.Count})";
            }

            if (collectionValueText != null)
            {
                float totalValue =
                    _collection.Sum(antique => antique.CurrentPrice);

                collectionValueText.text =
                    $"Wartość rynkowa: {totalValue:F0} zł";
            }
        }

        private void RefreshListings()
        {
            ClearListings();

            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                _currentPage,
                0,
                totalPages - 1);

            List<Antique> pageItems = _collection
                .Skip(_currentPage * ItemsPerPage)
                .Take(ItemsPerPage)
                .ToList();

            foreach (Antique antique in pageItems)
            {
                GameObject rowObject =
                    Instantiate(listingRowPrefab, listingsContainer);

                InventoryListItemUI rowUI =
                    rowObject.GetComponent<InventoryListItemUI>();

                if (rowUI == null)
                {
                    Debug.LogError(
                        "Inventory listing prefab does not contain InventoryListItemUI.",
                        rowObject);

                    Destroy(rowObject);
                    continue;
                }

                rowUI.Setup(antique, this);

                _spawnedRows.Add(rowUI);
            }
        }

        private void ClearListings()
        {
            foreach (InventoryListItemUI row in _spawnedRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _spawnedRows.Clear();
        }

        public void ShowDetails(Antique antique)
        {
            if (antique == null)
                return;

            if (collectionSummary != null)
                collectionSummary.SetActive(false);

            if (inventoryDetailsUI != null)
                inventoryDetailsUI.Show(antique);
            else
                Debug.LogError("InventoryView: InventoryDetailsUI is not assigned.");
        }

        public void ShowCollectionSummary()
        {
            if (inventoryDetailsUI != null)
                inventoryDetailsUI.gameObject.SetActive(false);

            if (collectionSummary != null)
                collectionSummary.SetActive(true);
        }

        private void SetupPaginationButtons()
        {
            if (previousButton != null)
                previousButton.onClick.AddListener(PreviousPage);

            if (nextButton != null)
                nextButton.onClick.AddListener(NextPage);

            if (firstPageButton != null)
            {
                firstPageButton.onClick.AddListener(
                    () => GoToPage(0));
            }

            if (lastPageButton != null)
            {
                lastPageButton.onClick.AddListener(
                    () => GoToPage(GetTotalPages() - 1));
            }

            if (previousPageNumberButton != null)
            {
                previousPageNumberButton.onClick.AddListener(
                    () => GoToPage(_currentPage - 1));
            }

            if (currentPageButton != null)
                currentPageButton.interactable = false;

            if (nextPageNumberButton != null)
            {
                nextPageNumberButton.onClick.AddListener(
                    () => GoToPage(_currentPage + 1));
            }
        }

        public void PreviousPage()
        {
            if (_currentPage <= 0)
                return;

            _currentPage--;

            RefreshListings();
            RefreshPagination();
        }

        public void NextPage()
        {
            int totalPages = GetTotalPages();

            if (_currentPage >= totalPages - 1)
                return;

            _currentPage++;

            RefreshListings();
            RefreshPagination();
        }

        private void GoToPage(int page)
        {
            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                page,
                0,
                totalPages - 1);

            RefreshListings();
            RefreshPagination();
        }

        private int GetTotalPages()
        {
            return Mathf.Max(
                1,
                Mathf.CeilToInt(
                    _collection.Count / (float)ItemsPerPage));
        }

        private void RefreshPagination()
        {
            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                _currentPage,
                0,
                totalPages - 1);

            int currentDisplayPage = _currentPage + 1;

            if (previousButton != null)
                previousButton.interactable = _currentPage > 0;

            if (nextButton != null)
            {
                nextButton.interactable =
                    _currentPage < totalPages - 1;
            }

            if (currentPageButton != null)
                currentPageButton.gameObject.SetActive(true);

            if (currentPageText != null)
                currentPageText.text = currentDisplayPage.ToString();

            bool showPreviousPage =
                currentDisplayPage > 1;

            if (previousPageNumberButton != null)
            {
                previousPageNumberButton.gameObject.SetActive(
                    showPreviousPage);
            }

            if (previousPageNumberText != null)
            {
                previousPageNumberText.text =
                    (currentDisplayPage - 1).ToString();
            }

            bool showNextPage =
                currentDisplayPage < totalPages;

            if (nextPageNumberButton != null)
            {
                nextPageNumberButton.gameObject.SetActive(
                    showNextPage);
            }

            if (nextPageNumberText != null)
            {
                nextPageNumberText.text =
                    (currentDisplayPage + 1).ToString();
            }

            bool showFirstPage =
                currentDisplayPage > 2;

            if (firstPageButton != null)
                firstPageButton.gameObject.SetActive(showFirstPage);

            if (firstPageText != null)
                firstPageText.text = "1";

            bool showLeftEllipsis =
                currentDisplayPage > 3;

            if (leftEllipsis != null)
            {
                leftEllipsis.gameObject.SetActive(
                    showLeftEllipsis);
            }

            bool showLastPage =
                currentDisplayPage < totalPages - 1;

            if (lastPageButton != null)
            {
                lastPageButton.gameObject.SetActive(
                    showLastPage);
            }

            if (lastPageText != null)
                lastPageText.text = totalPages.ToString();

            bool showRightEllipsis =
                currentDisplayPage < totalPages - 2;

            if (rightEllipsis != null)
            {
                rightEllipsis.gameObject.SetActive(
                    showRightEllipsis);
            }
        }
    }
}