using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public Century Century = Century.Unknown;

        public int Quantity;

        public ContractRequirement() { }

        public ContractRequirement(ContractAttributeScope scope, AntiqueType antiqueType, Country country, Century century, int quantity)
        {
            Scope = scope;
            AntiqueType = antiqueType;
            Country = country;
            Century = century;
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

        public ContractRequirement(ContractAttributeScope scope, Century century, int quantity)
        {
            Scope = scope;
            Century = century;
            Quantity = quantity;
        }


        public bool IsSatisfiedBy(Antique antique)
        {
            if (antique == null) return false;

            return Scope switch
            {
                ContractAttributeScope.AntiqueType => antique.Type == AntiqueType,
                ContractAttributeScope.Country => antique.Country == Country,
                ContractAttributeScope.Century => antique.Century == Century,
                _ => false
            };
        }

        public List<AntiqueDefinition> MatchingDefinitions()
        {
            return Scope switch
            {
                ContractAttributeScope.AntiqueType => AntiqueDatabase.GetByType(AntiqueType),
                ContractAttributeScope.Country => AntiqueDatabase.GetByCountry(Country),
                ContractAttributeScope.Century => AntiqueDatabase.GetByCentury(Century),
                _ => new List<AntiqueDefinition>()
            };
        }

        public float AverageReferenceUnitPrice(Market.Market market)
        {
            if (market == null) return 0f;

            var definitions = MatchingDefinitions();
            if (definitions == null || definitions.Count == 0) return 0f;

            float total = 0f;
            int counted = 0;

            foreach (var def in definitions)
            {
                if (def == null || def.BasePrice <= 0f) continue;

                var typeState = market.GetTypeState(def.Id);
                total += PriceEngine.CalculateReferencePrice(def.BasePrice, typeState);
                counted++;
            }

            return counted > 0 ? total / counted : 0f;
        }
    }
}