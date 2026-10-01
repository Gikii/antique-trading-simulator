using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public static class ContractDisplay
    {
        public static string ToDisplayString(this ContractType type) =>
            type == ContractType.Exclusive ? "Exclusive" : "Open";

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
