using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using UnityEngine;

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

        /// <summary>"4x Netherlands, min. 65% condition, 600 £+ each" – the one-line form for list rows.</summary>
        public static string RequirementSummary(ContractRequirement requirement)
        {
            if (requirement == null) return "";

            string summary = RequirementName(requirement);
            if (requirement.HasMinCondition)
                summary += $", min. {UIFormat.Percent(requirement.MinCondition)} condition";
            if (requirement.HasMinValue)
                summary += $", {UIFormat.Money(requirement.MinValue)}+ each";

            return summary;
        }

        /// <summary>"at least 600 £ each", or "any" for contracts without a value floor.</summary>
        public static string ValueRequirementShort(ContractRequirement requirement) =>
            requirement != null && requirement.HasMinValue
                ? $"at least {UIFormat.Money(requirement.MinValue)} each"
                : "any";

        /// <summary>"Value: at least 600 £ each", or "Value: any".</summary>
        public static string ValueRequirement(ContractRequirement requirement) =>
            $"Value: {ValueRequirementShort(requirement)}";

        /// <summary>
        /// Tooltip-style explanation of the value floor and its tolerance, generated from the numbers
        /// so it can't drift from the rule in ContractRequirement.
        /// </summary>
        public static string ValueRequirementHint(ContractRequirement requirement) =>
            requirement != null && requirement.HasMinValue
                ? $"Each antique's market value is checked when you hand it in. Anything worth at least " +
                  $"{UIFormat.Money(requirement.MinAcceptedValue)} counts " +
                  $"({UIFormat.Percent(ContractRequirement.MinValueTolerance)} of {UIFormat.Money(requirement.MinValue)}) — " +
                  "if the market falls, an antique bought for this contract may no longer qualify."
                : "";

        /// <summary>
        /// "+46 on delivery, −36 if failed" — the reputation at stake, from the same formulas TraderRecord
        /// applies. "" without settings. A lost race (someone else delivers an open contract first) costs nothing.
        /// </summary>
        public static string ReputationStakes(Contract contract, Company.ReputationSettings settings)
        {
            if (contract == null || settings == null) return "";

            int gain = settings.ReputationGainFor(contract);
            int loss = settings.ReputationLossFor(contract);

            return $"{UIFormat.Colorize($"+{gain}", UIFormat.PositiveColor)} on delivery, " +
                   $"{UIFormat.Colorize(loss.ToString(), UIFormat.NegativeColor)} if failed";
        }

        /// <summary>
        /// The body of the "Accept this contract?" dialog: what is asked, by when, what it pays, and what
        /// delivering or failing does — every number from the same rules the game applies.
        /// </summary>
        /// <param name="readyCount">Matching antiques the player could hand in right now.</param>
        /// <param name="activeContracts">Contracts the player has already accepted.</param>
        /// <param name="maxContracts">Their limit; negative = unlimited.</param>
        public static string AcceptConfirmation(Contract contract, Company.ReputationSettings settings, int currentWeek,
            int readyCount, int activeContracts, int maxContracts)
        {
            if (contract == null) return "";

            var req = contract.Requirement;
            var text = new System.Text.StringBuilder();

            // What and by when.
            text.Append($"<b>{RequirementName(req)}</b> · {TypeLabel(contract)}\n");

            var needs = new System.Collections.Generic.List<string>();
            if (req.HasMinCondition) needs.Add($"condition ≥ {UIFormat.Percent(req.MinCondition)}");
            if (req.HasMinValue) needs.Add($"worth ≥ {UIFormat.Money(req.MinValue)} each");
            if (needs.Count > 0) text.Append(Muted(string.Join(", ", needs))).Append('\n');

            int weeksLeft = Mathf.Max(0, contract.DeadlineWeek - currentWeek);
            text.Append($"Deliver before {UIFormat.GameDate(contract.DeadlineWeek)} ({UIFormat.Weeks(weeksLeft)} left).\n");

            string premium = PremiumText(contract);
            text.Append($"Payout {UIFormat.Colorize(UIFormat.Money(contract.TotalReward), UIFormat.PositiveColor)}");
            if (!string.IsNullOrEmpty(premium)) text.Append(Muted($" ({premium})"));
            text.Append("\n\n");

            // Consequences.
            if (settings != null)
            {
                text.Append("<b>If you deliver:</b> ")
                    .Append(UIFormat.Colorize($"+{settings.ReputationGainFor(contract)} reputation", UIFormat.PositiveColor))
                    .Append(", ")
                    .Append(UIFormat.Colorize($"{UIFormat.SignedPercent(settings.CredibilityGainFor(contract))} credibility", UIFormat.PositiveColor))
                    .Append(".\n");

                text.Append("<b>If the deadline passes:</b> ")
                    .Append(UIFormat.Colorize($"{settings.ReputationLossFor(contract)} reputation", UIFormat.NegativeColor))
                    .Append(", ")
                    .Append(UIFormat.Colorize($"{UIFormat.SignedPercent(settings.ContractFailedCredibility)} credibility", UIFormat.NegativeColor));

                if (contract.Penalty > 0f)
                    text.Append(" and a ").Append(UIFormat.Colorize($"{UIFormat.Money(contract.Penalty)} penalty", UIFormat.NegativeColor));

                text.Append(".\n");
            }
            else if (contract.Penalty > 0f)
            {
                text.Append("<b>If the deadline passes:</b> ")
                    .Append(UIFormat.Colorize($"{UIFormat.Money(contract.Penalty)} penalty", UIFormat.NegativeColor))
                    .Append(".\n");
            }

            text.Append(contract.Type == ContractType.Exclusive
                ? Muted("Exclusive: only you may deliver it once you accept.")
                : Muted("Open: another trader may deliver it first — then it's simply gone, at no cost to you."));
            text.Append("\n\n");

            // Where the player stands.
            text.Append($"You have {readyCount}/{req.Quantity} matching antiques ready");
            if (maxContracts >= 0)
                text.Append($" · contract slot {activeContracts + 1}/{maxContracts}");
            text.Append('.');

            return text.ToString();
        }

        private static string Muted(string text) => UIFormat.Colorize(text, UIFormat.MutedColor);

        /// <summary>"+1 320 £ over market value", or "" for contracts without a value floor.</summary>
        public static string PremiumText(Contract contract)
        {
            float? premium = contract?.Premium;
            return premium.HasValue ? $"{UIFormat.SignedMoney(premium.Value)} over market value" : "";
        }

        /// <summary>"4x Netherlands" or "3x Porcelain · 18th century" – without the condition, for headings that list it separately.</summary>
        public static string RequirementName(ContractRequirement requirement) =>
            requirement != null ? $"{requirement.Quantity}x {requirement.AttributesLabel()}" : "";

        /// <summary>The requirement's value for one attribute, or "Any" when it doesn't check that attribute.</summary>
        public static string AttributeValue(ContractRequirement requirement, ContractAttributeScope scope) =>
            requirement != null && requirement.Uses(scope) ? requirement.ValueLabel(scope) : "Any";

        /// <summary>"Condition: at least 70% (Good)", or "Condition: any".</summary>
        public static string ConditionRequirement(ContractRequirement requirement) =>
            requirement != null && requirement.HasMinCondition
                ? $"Condition: at least {UIFormat.Percent(requirement.MinCondition)} ({UIFormat.ConditionLabel(requirement.MinCondition)})"
                : "Condition: any";
    }
}
