using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using Newtonsoft.Json;
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
        /// <summary>
        /// The primary attribute: the one the contract was rolled from, used for its picture, the news
        /// it makes and as the fallback when <see cref="Attributes"/> is not set.
        /// </summary>
        public ContractAttributeScope Scope;

        /// <summary>
        /// Every attribute an antique has to match (the primary one plus, for a pair, a second one).
        /// None in saves made before pairs existed — <see cref="EffectiveAttributes"/> then falls back to Scope.
        /// </summary>
        public ContractAttributes Attributes;

        [JsonIgnore]
        public ContractAttributes EffectiveAttributes =>
            Attributes != ContractAttributes.None ? Attributes : Scope.ToFlag();

        [JsonIgnore]
        public bool IsPair => EffectiveAttributes.AttributeCount() > 1;

        public bool Uses(ContractAttributeScope scope) => EffectiveAttributes.Includes(scope);

        public AntiqueType AntiqueType = AntiqueType.Other;
        public Country Country = Country.Other;
        public Century Century = Century.Unknown;

        public int Quantity;

        /// <summary>
        /// Lowest condition (0..1) an antique must have to count toward the contract. 0 = any —
        /// also what contracts from saves made before this field existed get.
        /// </summary>
        public float MinCondition;

        [JsonIgnore]
        public bool HasMinCondition => MinCondition > 0f;

        /// <summary>
        /// Lowest market value (£) each antique must have — the class of object the client wants, and
        /// what the reward is priced from. Checked against the antique's current market value when it
        /// is handed in (with <see cref="MinValueTolerance"/> slack), so a falling market can void a
        /// planned delivery. 0 = any — also what contracts from saves made before this field existed get.
        /// </summary>
        public float MinValue;

        /// <summary>Share of MinValue an antique still passes with, so small market swings don't void a delivery.</summary>
        public const float MinValueTolerance = 0.9f;

        [JsonIgnore]
        public bool HasMinValue => MinValue > 0f;

        /// <summary>The value an antique actually needs: MinValue less the tolerance.</summary>
        [JsonIgnore]
        public float MinAcceptedValue => MinValue * MinValueTolerance;

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


        /// <summary>"3x Porcelain · 18th century" — same wording as ContractDisplay.RequirementName.</summary>
        public override string ToString() => $"{Quantity}x {AttributesLabel()}";

        /// <summary>"Porcelain", or "Porcelain · 18th century" for a pair (type, century, country order).</summary>
        public string AttributesLabel()
        {
            var parts = new List<string>(3);
            foreach (var scope in ContractAttributesExtensions.DisplayOrder)
                if (Uses(scope)) parts.Add(ValueLabel(scope));

            return parts.Count > 0 ? string.Join(" · ", parts) : "Antiques";
        }

        /// <summary>The display name of the value this requirement has for one attribute.</summary>
        public string ValueLabel(ContractAttributeScope scope) => scope switch
        {
            ContractAttributeScope.AntiqueType => AntiqueType.ToDisplayString(),
            ContractAttributeScope.Country => Country.ToDisplayString(),
            ContractAttributeScope.Century => Century.ToDisplayString(),
            _ => ""
        };

        /// <summary>True if an antique (or a type) with these attributes matches every attribute the requirement uses.</summary>
        public bool MatchesAttributes(AntiqueType type, Country country, Century century)
        {
            var attributes = EffectiveAttributes;
            if (attributes == ContractAttributes.None) return false;

            if (attributes.Includes(ContractAttributeScope.AntiqueType) && type != AntiqueType) return false;
            if (attributes.Includes(ContractAttributeScope.Country) && country != Country) return false;
            if (attributes.Includes(ContractAttributeScope.Century) && century != Century) return false;
            return true;
        }

        public bool IsSatisfiedBy(Antique antique)
        {
            if (antique == null) return false;

            // Small tolerance so an antique shown as "70%" meets a 70% requirement.
            if (antique.Condition + 0.0005f < MinCondition) return false;

            if (!MeetsMinValue(antique)) return false;

            return MatchesAttributes(antique.Type, antique.Country, antique.Century);
        }

        /// <summary>
        /// Market value (CurrentPrice, never an asking price) against the value floor. Half a pound of
        /// slack so an antique shown as "540 £" meets a 540 £ requirement.
        /// </summary>
        public bool MeetsMinValue(Antique antique) =>
            antique != null && (!HasMinValue || antique.CurrentPrice + 0.5f >= MinAcceptedValue);

        /// <summary>
        /// What one qualifying antique roughly costs: the value floor. Contracts without one (older
        /// saves) fall back to the average reference price of the matching types.
        /// </summary>
        public float ExpectedUnitCost(Market.Market market) =>
            HasMinValue ? MinValue : AverageReferenceUnitPrice(market);

        /// <summary>Every antique type that matches all the attributes this requirement uses.</summary>
        public List<AntiqueDefinition> MatchingDefinitions()
        {
            var all = AntiqueDatabase.GetAll();
            if (all == null) return new List<AntiqueDefinition>();

            return all.Where(def => def != null && MatchesAttributes(def.Type, def.Country, def.Century)).ToList();
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