using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// The player's trading agent. Makes no decisions on its own — UI code
    /// (button onClick handlers, etc.) calls the inherited BuyListing/SellListing
    /// directly with the Id of whatever listing was clicked.
    /// </summary>
    public class PlayerTrader : TraderAgent
    {
        [SerializeField] private ContractManager contractManager;

        protected override void Awake()
        {
            base.Awake();

            if (contractManager == null)
                contractManager = FindFirstObjectByType<ContractManager>();

            contractManager?.RegisterTrader(Antique.PlayerOwnerId, Inventory);
        }

        public bool AcceptContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId) || Inventory.IsCommittedToContract(contractId))
                return false;

            var contract = contractManager != null ? contractManager.GetById(contractId) : null;
            if (contract == null || contract.Status != ContractStatus.Active)
                return false;

            if (contract.Type == ContractType.Exclusive &&
                !contractManager.ClaimContract(contractId, Antique.PlayerOwnerId))
                return false;

            return Inventory.CommitToContract(contractId);
        }
    }
}
