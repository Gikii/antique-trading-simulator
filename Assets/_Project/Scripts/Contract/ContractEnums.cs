using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    public enum ContractType
    {
        Open,
        Exclusive
    }
    public enum ContractStatus
    {
        Active,
        Fulfilled,
        Expired,
    }

    public enum ContractAttributeScope
    {
        AntiqueType,
        Country,
        Century
    }

    /// <summary>
    /// Which attributes a requirement checks — one ("Porcelain") or a pair ("Porcelain · 18th century").
    /// None = not set (contracts from saves made before pairs existed): derived from the Scope then.
    /// </summary>
    [Flags]
    public enum ContractAttributes
    {
        None = 0,
        AntiqueType = 1 << 0,
        Country = 1 << 1,
        Century = 1 << 2
    }

    public static class ContractAttributesExtensions
    {
        /// <summary>Display order of the attributes: type, then century, then country ("Porcelain · 18th century · France").</summary>
        public static readonly ContractAttributeScope[] DisplayOrder =
        {
            ContractAttributeScope.AntiqueType,
            ContractAttributeScope.Century,
            ContractAttributeScope.Country
        };

        public static ContractAttributes ToFlag(this ContractAttributeScope scope) => scope switch
        {
            ContractAttributeScope.AntiqueType => ContractAttributes.AntiqueType,
            ContractAttributeScope.Country => ContractAttributes.Country,
            ContractAttributeScope.Century => ContractAttributes.Century,
            _ => ContractAttributes.None
        };

        public static bool Includes(this ContractAttributes attributes, ContractAttributeScope scope) =>
            (attributes & scope.ToFlag()) != 0;

        public static int AttributeCount(this ContractAttributes attributes)
        {
            int count = 0;
            foreach (var scope in DisplayOrder)
                if (attributes.Includes(scope)) count++;
            return count;
        }
    }

}