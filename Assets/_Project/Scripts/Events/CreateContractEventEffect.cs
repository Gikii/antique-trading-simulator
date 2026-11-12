using UnityEngine;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

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

        [Tooltip("Scope of the contract. Leave at Random to roll a scope.")]
        public RequirementScope Scope = RequirementScope.Random;

        [Tooltip("Used when the resolved scope is AntiqueType. Leave at Other to roll a type.")]
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Tooltip("Used when the resolved scope is Century. Leave at Unknown to roll a century.")]
        public Century Century = Century.Unknown;

        [Tooltip("Used when the resolved scope is Country. Leave at Other to roll a country.")]
        public Country Country = Country.Other;

        [Tooltip("How many antiques the client wants. 0 = rolled from the contract class's range.")]
        [Min(0)] public int Quantity = 0;

        [Tooltip("0 = rolled from the contract class's range.")]
        [Range(0f, Antique.MaxCondition)]
        public float MinCondition = 0f;

        [Tooltip("Days the player gets. 0 = rolled. A value above the ContractManager's Max Duration Days is honoured.")]
        [Min(0)] public int DurationDays = 0;

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
                if (contractManager.GenerateContract(context.CurrentDay, overrides) != null)
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
                DurationDays = DurationDays,
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

            if (scope == ContractAttributeScope.AntiqueType && AntiqueType != AntiqueType.Other)
                overrides.AntiqueType = AntiqueType;
            if (scope == ContractAttributeScope.Country && Country != Country.Other)
                overrides.Country = Country;
            if (scope == ContractAttributeScope.Century && Century != Century.Unknown)
                overrides.Century = Century;

            if (Quantity > 0) overrides.Quantity = Quantity;
            if (MinCondition > 0f) overrides.MinCondition = MinCondition;
            if (DurationDays > 0) overrides.DurationDays = DurationDays;
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
