using System;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Tracks the dynamic, type-level Supply and Demand for one AntiqueDefinition.
    /// This is what used to live on the old per-type "Antique" runtime object. Now that
    /// each physical antique is its own AntiqueListing, Supply/Demand no longer represent
    /// "how many are left to buy" — instead they represent overall market conditions for
    /// that type of antique, which drive two things:
    ///   1) The odds that a new listing of this type appears (see Market.GenerateListing).
    ///   2) The supply/demand price multiplier applied to every listing of this type.
    /// </summary>
    [Serializable]
    public class AntiqueMarketState
    {
        public string DefinitionId;
        public float Supply;
        public float Demand;

        public AntiqueMarketState(string definitionId, float initialSupply, float initialDemand)
        {
            DefinitionId = definitionId;
            Supply = initialSupply;
            Demand = initialDemand;
        }
    }
}
