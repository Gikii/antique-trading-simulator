using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public static class ContractDisplay
    {
        public static string ToDisplayString(this ContractType type) =>
            type == ContractType.Exclusive ? "Exclusive" : "Open";

        /// <summary>"Exclusive", or "Exclusive · Premium" for a contract of a reputation-gated class.</summary>
        public static string TypeLabel(Contract contract)
        {
            if (contract == null) return "";
            string type = contract.Type.ToDisplayString();
            return string.IsNullOrEmpty(contract.ClassName) ? type : $"{type} · {contract.ClassName}";
        }

        public static string RequirementSummary(ContractRequirement requirement)
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
    }
}
