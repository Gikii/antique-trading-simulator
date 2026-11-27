namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// The decision constants the NPC logic and the telemetry mirrors share, so both judge a
    /// trade by one set of rules.
    /// </summary>
    public readonly struct TraderPolicy
    {
        /// <summary>Exclusive contracts must leave at least this share of the reward as profit.</summary>
        public readonly float ExclusiveMinProfitShare;

        /// <summary>Share of cash a trader may tie up in contract purchases at once.</summary>
        public readonly float ContractCashShareBase;

        /// <summary>How much of its cash RiskTolerance 1 adds to ContractCashShareBase.</summary>
        public readonly float ContractCashShareRiskSpan;

        /// <summary>How far RiskTolerance 1 lifts the price ceiling when buying to fill a contract.</summary>
        public readonly float ContractRiskPremium;

        /// <summary>A discretionary purchase may not take cash below the reserves times this.</summary>
        public readonly float ReserveSafetyMargin;

        /// <summary>The return hurdle is multiplied by this while a contract is outstanding.</summary>
        public readonly float CommittedBargainPenalty;

        public TraderPolicy(float exclusiveMinProfitShare, float contractCashShareBase,
            float contractCashShareRiskSpan, float contractRiskPremium,
            float reserveSafetyMargin, float committedBargainPenalty)
        {
            ExclusiveMinProfitShare = exclusiveMinProfitShare;
            ContractCashShareBase = contractCashShareBase;
            ContractCashShareRiskSpan = contractCashShareRiskSpan;
            ContractRiskPremium = contractRiskPremium;
            ReserveSafetyMargin = reserveSafetyMargin;
            CommittedBargainPenalty = committedBargainPenalty;
        }

        /// <summary>
        /// The live rules. Get-only, so a -set: override cannot half-write the struct mid-run.
        /// </summary>
        public static TraderPolicy Default { get; } = new TraderPolicy(0.15f, 0.40f, 0.35f, 0.35f, 1.25f, 2f);
    }
}
