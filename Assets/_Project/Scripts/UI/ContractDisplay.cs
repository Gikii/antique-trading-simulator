using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Shared formatting helpers for contract UI, so the list rows and the
    /// details panel describe a contract's type/requirement the same way.
    /// </summary>
    public static class ContractDisplay
    {
        public static string ToDisplayString(this ContractType type) =>
            type == ContractType.Exclusive ? "Exclusive" : "Open";

        /// <summary>
        /// Short one-line summary of what a requirement asks for, e.g.
        /// "3x Porcelain", "2x France", "5x XVIII century" — used as a
        /// stand-in title for list rows until contracts have real
        /// generated names (see Contract.cs — there is no Name yet).
        /// </summary>
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
