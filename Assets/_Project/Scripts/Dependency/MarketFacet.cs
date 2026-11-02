using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>Names the enum a FacetKind member stands for.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FacetEnumAttribute : Attribute
    {
        public Type EnumType { get; }

        public FacetEnumAttribute(Type enumType)
        {
            EnumType = enumType;
        }
    }

    /// <summary>
    /// The dimensions antiques can be grouped by. Each member names its enum from AntiqueEnums,
    /// and AntiqueDefinition must have a public field of that enum type.
    /// </summary>
    public enum FacetKind
    {
        [FacetEnum(typeof(AntiqueEnums.AntiqueType))] Type = 0,
        [FacetEnum(typeof(AntiqueEnums.Century))] Century = 1,
        [FacetEnum(typeof(AntiqueEnums.Country))] Country = 2,

        // A new dimension (Material, Style, Author, ...): add the enum to AntiqueEnums, add the
        // field to AntiqueDefinition, add one member here.
    }

    /// <summary>
    /// One category: "paintings", "the XIX century", "France". Kind says which enum, Value is that
    /// enum member as an int, so a single type addresses every dimension.
    /// </summary>
    [Serializable]
    public struct MarketFacet : IEquatable<MarketFacet>
    {
        public FacetKind Kind;
        public int Value;

        public MarketFacet(FacetKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        public static MarketFacet Of(AntiqueEnums.AntiqueType type) => new MarketFacet(FacetKind.Type, (int)type);
        public static MarketFacet Of(AntiqueEnums.Century century) => new MarketFacet(FacetKind.Century, (int)century);
        public static MarketFacet Of(AntiqueEnums.Country country) => new MarketFacet(FacetKind.Country, (int)country);

        /// <summary>True for the enum's "unspecified" member (Other / Unknown, always 0).</summary>
        public bool IsDefaultValue => Value == 0;

        public bool Equals(MarketFacet other) => Kind == other.Kind && Value == other.Value;

        public override bool Equals(object obj) => obj is MarketFacet other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Value;

        public static bool operator ==(MarketFacet a, MarketFacet b) => a.Equals(b);
        public static bool operator !=(MarketFacet a, MarketFacet b) => !a.Equals(b);

        public override string ToString() => MarketFacets.DisplayName(this);
    }

    /// <summary>
    /// Turns a category into the antique definitions inside it, and back. Lookups are cached, so
    /// clear the cache after the antique assets change.
    /// </summary>
    public static class MarketFacets
    {
        private static readonly Dictionary<FacetKind, Type> _enumTypes = new Dictionary<FacetKind, Type>();
        private static readonly Dictionary<FacetKind, Func<AntiqueDefinition, int>> _selectors = new Dictionary<FacetKind, Func<AntiqueDefinition, int>>();
        private static readonly Dictionary<FacetKind, int[]> _values = new Dictionary<FacetKind, int[]>();
        private static readonly Dictionary<MarketFacet, List<string>> _definitionIdsByFacet = new Dictionary<MarketFacet, List<string>>();
        private static readonly Dictionary<string, MarketFacet[]> _facetsByDefinitionId = new Dictionary<string, MarketFacet[]>();

        private static readonly FacetKind[] _allKinds = (FacetKind[])Enum.GetValues(typeof(FacetKind));

        public static IReadOnlyList<FacetKind> AllKinds => _allKinds;

        /// <summary>
        /// Overrides how a dimension is read off a definition, for categories computed elsewhere
        /// in the game.
        /// </summary>
        public static void RegisterSelector(FacetKind kind, Func<AntiqueDefinition, int> selector)
        {
            if (selector == null) return;
            _selectors[kind] = selector;
            ClearCache();
        }

        /// <summary>The enum backing this dimension, taken from the FacetEnum attribute.</summary>
        public static Type EnumTypeOf(FacetKind kind)
        {
            if (_enumTypes.TryGetValue(kind, out var cached))
                return cached;

            var field = typeof(FacetKind).GetField(kind.ToString(), BindingFlags.Public | BindingFlags.Static);
            var attribute = field?.GetCustomAttribute<FacetEnumAttribute>();

            if (attribute == null || !attribute.EnumType.IsEnum)
                Debug.LogError($"MarketFacets: FacetKind.{kind} has no valid [FacetEnum(typeof(SomeEnum))] attribute — it cannot be resolved to antiques.");

            var enumType = attribute != null && attribute.EnumType.IsEnum ? attribute.EnumType : null;
            _enumTypes[kind] = enumType;
            return enumType;
        }

        /// <summary>Every member of this dimension's enum as an int (Century runs 12..20).</summary>
        public static int[] ValuesOf(FacetKind kind)
        {
            if (_values.TryGetValue(kind, out var cached))
                return cached;

            var enumType = EnumTypeOf(kind);
            int[] values = enumType == null
                ? Array.Empty<int>()
                : Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32).ToArray();

            _values[kind] = values;
            return values;
        }

        public static string DisplayName(MarketFacet facet)
        {
            var enumType = EnumTypeOf(facet.Kind);
            string name = enumType != null ? Enum.GetName(enumType, facet.Value) : null;
            return $"{facet.Kind}:{name ?? facet.Value.ToString()}";
        }

        /// <summary>
        /// How this dimension is read off a definition: the public field on AntiqueDefinition whose
        /// type is the dimension's enum, found by reflection.
        /// </summary>
        private static Func<AntiqueDefinition, int> SelectorOf(FacetKind kind)
        {
            if (_selectors.TryGetValue(kind, out var cached))
                return cached;

            var enumType = EnumTypeOf(kind);
            Func<AntiqueDefinition, int> selector = null;

            if (enumType != null)
            {
                var fields = typeof(AntiqueDefinition)
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.FieldType == enumType)
                    .ToList();

                if (fields.Count == 0)
                {
                    Debug.LogError($"MarketFacets: AntiqueDefinition has no public field of type {enumType.Name}, so FacetKind.{kind} matches nothing. Add the field, or register a custom selector.");
                }
                else
                {
                    if (fields.Count > 1)
                        Debug.LogWarning($"MarketFacets: AntiqueDefinition has {fields.Count} fields of type {enumType.Name}; using '{fields[0].Name}'. Register a custom selector to pick another.");

                    var field = fields[0];
                    selector = definition => Convert.ToInt32(field.GetValue(definition));
                }
            }

            _selectors[kind] = selector;
            return selector;
        }

        /// <summary>Ids of every antique definition in this category; empty when it holds none.</summary>
        public static IReadOnlyList<string> ResolveDefinitionIds(MarketFacet facet)
        {
            if (_definitionIdsByFacet.TryGetValue(facet, out var cached))
                return cached;

            var ids = new List<string>();
            var selector = SelectorOf(facet.Kind);

            if (selector != null)
            {
                foreach (var definition in AntiqueDatabase.GetAll())
                {
                    if (definition == null) continue;
                    if (selector(definition) == facet.Value)
                        ids.Add(definition.Id);
                }
            }

            _definitionIdsByFacet[facet] = ids;
            return ids;
        }

        /// <summary>How many antique definitions fall under this category.</summary>
        public static int MemberCount(MarketFacet facet) => ResolveDefinitionIds(facet).Count;

        /// <summary>Every category a definition belongs to, one per dimension.</summary>
        public static IReadOnlyList<MarketFacet> FacetsOf(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
                return Array.Empty<MarketFacet>();

            if (_facetsByDefinitionId.TryGetValue(definitionId, out var cached))
                return cached;

            var definition = AntiqueDatabase.GetById(definitionId);
            MarketFacet[] facets;

            if (definition == null)
            {
                facets = Array.Empty<MarketFacet>();
            }
            else
            {
                var list = new List<MarketFacet>(_allKinds.Length);
                foreach (var kind in _allKinds)
                {
                    var selector = SelectorOf(kind);
                    if (selector == null) continue;
                    list.Add(new MarketFacet(kind, selector(definition)));
                }
                facets = list.ToArray();
            }

            _facetsByDefinitionId[definitionId] = facets;
            return facets;
        }

        /// <summary>The categories of a dimension that at least one definition uses.</summary>
        public static List<MarketFacet> PopulatedFacets(FacetKind kind)
        {
            var result = new List<MarketFacet>();
            foreach (int value in ValuesOf(kind))
            {
                var facet = new MarketFacet(kind, value);
                if (MemberCount(facet) > 0)
                    result.Add(facet);
            }
            return result;
        }

        /// <summary>Drops the cached lookups.</summary>
        public static void ClearCache()
        {
            _definitionIdsByFacet.Clear();
            _facetsByDefinitionId.Clear();
        }
    }
}
