using UnityEngine;

namespace AntiqueTradingSimulator.Orders
{
    public enum OrderType
    {
        Open,
        Exclusive
    }
    public enum OrderStatus
    {
        Active,
        Fulfilled,
        Expired,
    }

    public enum OrderAttributeScope
    {
        AntiqueType,
        Country,
        TimePeriod
    }

}