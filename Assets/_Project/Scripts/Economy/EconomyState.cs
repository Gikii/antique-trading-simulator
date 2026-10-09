using System.Collections.Generic;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Economy
{
    public class EconomyState
    {
        public Dictionary<string, TraderInventoryState> Inventories = new Dictionary<string, TraderInventoryState>();
        public MarketState Market;
    }
    public class TraderInventoryState
    {
        public float Cash;
        public int LastKnownDay;

        public List<AntiqueState> Holdings = new List<AntiqueState>();

        public LedgerState Ledger = new LedgerState();

        public List<PricePoint> ValueHistory = new List<PricePoint>();

        public List<string> CommittedContractIds = new List<string>();
        public WarehouseState Warehouse;
    }

    public class LedgerState
    {
        public List<LedgerEntry> Entries = new List<LedgerEntry>();
    }
}
