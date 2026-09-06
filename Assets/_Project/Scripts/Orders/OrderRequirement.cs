using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Orders;
using System;
using System.Collections.Generic;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Orders
{
    [Serializable]
    public class OrderRequirement
    {
        public OrderAttributeScope Scope;

        public AntiqueType AntiqueType = AntiqueType.Other;
        public Country Country = Country.Other;
        public TimePeriod TimePeriod = TimePeriod.Unknown;

        public int Quantity;

        public OrderRequirement() { }

        public OrderRequirement(OrderAttributeScope scope, AntiqueType antiqueType, Country country, TimePeriod timePeriod, int quantity)
        {
            Scope = scope;
            AntiqueType = antiqueType;
            Country = country;
            TimePeriod = timePeriod;
            Quantity = quantity;
        }

        public OrderRequirement(OrderAttributeScope scope, AntiqueType antiqueType, int quantity)
        {
            Scope = scope;
            AntiqueType = antiqueType;
            Quantity = quantity;
        }

        public OrderRequirement(OrderAttributeScope scope, Country country, int quantity)
        {
            Scope = scope;
            Country = country;
            Quantity = quantity;
        }

        public OrderRequirement(OrderAttributeScope scope, TimePeriod timePeriod, int quantity)
        {
            Scope = scope;
            TimePeriod = timePeriod;
            Quantity = quantity;
        }


        public bool IsSatisfiedBy(Antique antique)
        {
            if (antique == null) return false;

            return Scope switch
            {
                OrderAttributeScope.AntiqueType => antique.Type == AntiqueType,
                OrderAttributeScope.Country => antique.Country == Country,
                OrderAttributeScope.TimePeriod => antique.TimePeriod == TimePeriod,
                _ => false
            };
        }

        public List<AntiqueDefinition> MatchingDefinitions()
        {
            return Scope switch
            {
                OrderAttributeScope.AntiqueType => AntiqueDatabase.GetByType(AntiqueType),
                OrderAttributeScope.Country => AntiqueDatabase.GetByCountry(Country),
                OrderAttributeScope.TimePeriod => AntiqueDatabase.GetByTimePeriod(TimePeriod),
                _ => new List<AntiqueDefinition>()
            };
        }
    }
}
