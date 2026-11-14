using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public static class ContractDisplay
    {
        public static string ToDisplayString(this ContractType type) =>
            type == ContractType.Exclusive ? "Exclusive" : "Open";

        public static string ContractName(Contract contract) => contract != null ? contract.DisplayName : "";

        /// <summary>"Exclusive", or "Exclusive · Premium" for a contract of a reputation-gated class.</summary>
        public static string TypeLabel(Contract contract)
        {
            if (contract == null) return "";
            string type = contract.Type.ToDisplayString();
            return string.IsNullOrEmpty(contract.ClassName) ? type : $"{type} · {contract.ClassName}";
        }

        /// <summary>"4x Netherlands, min. 65% condition" – the one-line form for list rows.</summary>
        public static string RequirementSummary(ContractRequirement requirement)
        {
            if (requirement == null) return "";

            return requirement.HasMinCondition
                ? $"{RequirementName(requirement)}, min. {UIFormat.Percent(requirement.MinCondition)} condition"
                : RequirementName(requirement);
        }

        /// <summary>"4x Netherlands" – without the condition, for headings that list the condition separately.</summary>
        public static string RequirementName(ContractRequirement requirement)
        {
            if (requirement == null) return "";

            string what = requirement.Scope switch
            {
                ContractAttributeScope.AntiqueType => requirement.AntiqueType.ToDisplayString(),
                ContractAttributeScope.Country => requirement.Country.ToDisplayString(),
                ContractAttributeScope.Century => requirement.Century.ToDisplayString(),
                _ => "Antiques"
            };

            return $"{requirement.Quantity}x {what}";
        }

        /// <summary>"Condition: at least 70% (Good)", or "Condition: any".</summary>
        public static string ConditionRequirement(ContractRequirement requirement) =>
            requirement != null && requirement.HasMinCondition
                ? $"Condition: at least {UIFormat.Percent(requirement.MinCondition)} ({UIFormat.ConditionLabel(requirement.MinCondition)})"
                : "Condition: any";
    }
}
