using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    public enum ContractType
    {
        Open,
        Exclusive
    }
    public enum ContractStatus
    {
        Active,
        Fulfilled,
        Expired,
    }

    public enum ContractAttributeScope
    {
        AntiqueType,
        Country,
        TimePeriod
    }

}