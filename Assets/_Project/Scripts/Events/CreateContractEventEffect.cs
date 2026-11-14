using UnityEngine;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using static AntiqueTradingSimulator.Market.AntiqueEnums;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Events
{

    [Serializable]
    public class CreateContractEventEffect : EventEffect
    {
        public enum RequirementScope
        {
            Random = 0,
            AntiqueType,
            Country,
            Century
        }

        public enum ContractTypeOption
        {
            Random = 0,
            Open,
            Exclusive
        }

        public const string StandardClassName = "Standard";

        [Tooltip("Minimum number of contracts created.")]
        public int MinCount = 1;
        [Tooltip("Maximum number of contracts created.")]
        public int MaxCount = 1;

        [Tooltip("Primary attribute of the contract (its picture and news). Leave at Random to roll one — " +
                 "or to use the first attribute given a value below.")]
        public RequirementScope Scope = RequirementScope.Random;

        [Tooltip("Required type. Leave at Other to leave it out (or roll it, when it's the Scope). " +
                 "Setting two of type / century / country makes a pair, e.g. Porcelain · 18th century.")]
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Tooltip("Required century. Leave at Unknown to leave it out (or roll it, when it's the Scope).")]
        public Century Century = Century.Unknown;

        [Tooltip("Required country. Leave at Other to leave it out (or roll it, when it's the Scope).")]
        public Country Country = Country.Other;

        [Tooltip("How many antiques the client wants. 0 = rolled from the contract class's range.")]
        [Min(0)] public int Quantity = 0;

        [Tooltip("0 = rolled from the contract class's range.")]
        [Range(0f, Antique.MaxCondition)]
        public float MinCondition = 0f;

        [Tooltip("Lowest market value (£) each antique must have; the reward is priced from it. 0 = rolled from the contract class's value range.")]
        [Min(0f)] public float MinValue = 0f;

        [Tooltip("Weeks the player gets. 0 = rolled. A value above the ContractManager's Max Duration Days is honoured.")]
        [FormerlySerializedAs("DurationDays")]
        [Min(0)] public int DurationWeeks = 0;

        [Tooltip("Payment per antique delivered. 0 = Price calculated normally.")]
        [Min(0f)] public float RewardPerUnit = 0f;

      
        public ContractTypeOption Type = ContractTypeOption.Random;

        [Tooltip("Negative = calculated based on reward.")]
        public float Penalty = -1f;

        public string ClassName = "";

        [Tooltip("Reputation the player needs to accept it. Negative = based on contract class.")]
        public int RequiredReputation = -1;

        public override void Apply(EventContext context)
        {
            var contractManager = context.ContractManager;
            if (contractManager == null)
            {
                Debug.LogWarning("CreateContractEventEffect: no contract manager available — no contract was created.");
                return;
            }

            int min = Mathf.Max(0, Mathf.Min(MinCount, MaxCount));
            int max = Mathf.Max(min, MaxCount);
            int count = UnityEngine.Random.Range(min, max + 1);

            ContractOverrides overrides = BuildOverrides();

            int created = 0;
            for (int i = 0; i < count; i++)
            {
                if (contractManager.GenerateContract(context.CurrentWeek, overrides) != null)
                    created++;
            }

            if (created < count)
                Debug.LogWarning("CreateContractEventEffect: not every contract could be created — the usual cause " +
                                 "is that no antique matching the requirement has a market price yet.");

            Debug.Log($"CreateContractEventEffect: created {created}/{count} contract(s).");
        }

        public override void Revert(EventContext context)
        {
        }

        public override NewsEventData CreateNewsData()
        {
            return new NewsEventData(NewsScope(), AntiqueType, Country, Century, true);
        }

        public override EventEffect Clone()
        {
            return new CreateContractEventEffect
            {
                MinCount = MinCount,
                MaxCount = MaxCount,
                Scope = Scope,
                AntiqueType = AntiqueType,
                Century = Century,
                Country = Country,
                Quantity = Quantity,
                MinCondition = MinCondition,
                MinValue = MinValue,
                DurationWeeks = DurationWeeks,
                RewardPerUnit = RewardPerUnit,
                Type = Type,
                Penalty = Penalty,
                ClassName = ClassName,
                RequiredReputation = RequiredReputation
            };
        }

        private ContractOverrides BuildOverrides()
        {
            ContractAttributeScope? scope = ResolveScope();

            var overrides = new ContractOverrides
            {
                Scope = scope,
                ClassName = ResolveClassName()
            };

            // Every attribute given a value is required — the Scope's one first, any others with it
            // (AntiqueType = Porcelain + Century = 18th → "Porcelain · 18th century").
            var attributes = ContractAttributes.None;
            if (AntiqueType != AntiqueType.Other)
            {
                overrides.AntiqueType = AntiqueType;
                attributes |= ContractAttributes.AntiqueType;
            }
            if (Country != Country.Other)
            {
                overrides.Country = Country;
                attributes |= ContractAttributes.Country;
            }
            if (Century != Century.Unknown)
            {
                overrides.Century = Century;
                attributes |= ContractAttributes.Century;
            }

            if (scope.HasValue)
            {
                attributes |= scope.Value.ToFlag();
                // Exactly the attributes given — no random second one on top of a designed contract.
                overrides.Attributes = attributes;
            }

            if (Quantity > 0) overrides.Quantity = Quantity;
            if (MinCondition > 0f) overrides.MinCondition = MinCondition;
            if (MinValue > 0f) overrides.MinValue = MinValue;
            if (DurationWeeks > 0) overrides.DurationWeeks = DurationWeeks;
            if (RewardPerUnit > 0f) overrides.RewardPerUnit = RewardPerUnit;
            if (Penalty >= 0f) overrides.Penalty = Penalty;
            if (RequiredReputation >= 0) overrides.RequiredReputation = RequiredReputation;

            if (Type == ContractTypeOption.Open) overrides.Type = ContractType.Open;
            else if (Type == ContractTypeOption.Exclusive) overrides.Type = ContractType.Exclusive;

            return overrides;
        }

        private ContractAttributeScope? ResolveScope()
        {
            switch (Scope)
            {
                case RequirementScope.AntiqueType: return ContractAttributeScope.AntiqueType;
                case RequirementScope.Country: return ContractAttributeScope.Country;
                case RequirementScope.Century: return ContractAttributeScope.Century;
            }

            if (AntiqueType != AntiqueType.Other) return ContractAttributeScope.AntiqueType;
            if (Country != Country.Other) return ContractAttributeScope.Country;
            if (Century != Century.Unknown) return ContractAttributeScope.Century;
            return null;
        }

        private string ResolveClassName()
        {
            if (string.IsNullOrWhiteSpace(ClassName)) return null;

            string trimmed = ClassName.Trim();
            return string.Equals(trimmed, StandardClassName, StringComparison.OrdinalIgnoreCase) ? "" : trimmed;
        }

        private TargetScope NewsScope()
        {
            return ResolveScope() switch
            {
                ContractAttributeScope.AntiqueType => AntiqueType != AntiqueType.Other ? TargetScope.AntiqueType : TargetScope.Other,
                ContractAttributeScope.Country => Country != Country.Other ? TargetScope.Country : TargetScope.Other,
                ContractAttributeScope.Century => Century != Century.Unknown ? TargetScope.Century : TargetScope.Other,
                _ => TargetScope.Other
            };
        }
    }
}
