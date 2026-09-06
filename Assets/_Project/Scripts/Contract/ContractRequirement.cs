using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Contracts;
using System;
using System.Collections.Generic;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Contracts
{
    [Serializable]
    public class ContractRequirement
    {
        public ContractAttributeScope Scope;

        public AntiqueType AntiqueType = AntiqueType.Other;
        public Country Country = Country.Other;
        public TimePeriod TimePeriod = TimePeriod.Unknown;

        public int Quantity;

        public ContractRequirement() { }

        public ContractRequirement(ContractAttributeScope scope, AntiqueType antiqueType, Country country, TimePeriod timePeriod, int quantity)
        {
            Scope = scope;
            AntiqueType = antiqueType;
            Country = country;
            TimePeriod = timePeriod;
            Quantity = quantity;
        }

        public ContractRequirement(ContractAttributeScope scope, AntiqueType antiqueType, int quantity)
        {
            Scope = scope;
            AntiqueType = antiqueType;
            Quantity = quantity;
        }

        public ContractRequirement(ContractAttributeScope scope, Country country, int quantity)
        {
            Scope = scope;
            Country = country;
            Quantity = quantity;
        }

        public ContractRequirement(ContractAttributeScope scope, TimePeriod timePeriod, int quantity)
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
                ContractAttributeScope.AntiqueType => antique.Type == AntiqueType,
                ContractAttributeScope.Country => antique.Country == Country,
                ContractAttributeScope.TimePeriod => antique.TimePeriod == TimePeriod,
                _ => false
            };
        }

        public List<AntiqueDefinition> MatchingDefinitions()
        {
            return Scope switch
            {
                ContractAttributeScope.AntiqueType => AntiqueDatabase.GetByType(AntiqueType),
                ContractAttributeScope.Country => AntiqueDatabase.GetByCountry(Country),
                ContractAttributeScope.TimePeriod => AntiqueDatabase.GetByTimePeriod(TimePeriod),
                _ => new List<AntiqueDefinition>()
            };
        }
    }
}
