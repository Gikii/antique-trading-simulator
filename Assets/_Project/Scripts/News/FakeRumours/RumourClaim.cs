using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Rumours
{
    public enum RumourDirection
    {
        Rise = 0,
        Fall = 1
    }

    /// <summary>
    /// What a rumour claims, whether the antiques' prices will rise or fall, and the antique types, countries and centuries it names.
    ///
    /// The named values are a UNION: "clocks and French pieces" covers every clock and every French
    /// antique, not only French clocks. So naming another attribute or another value widens the claim,
    /// which is what makes both cost more (FakeRumourSettings.ClaimWeight).
    /// </summary>
    public class RumourClaim
    {
        public RumourDirection Direction;

        public readonly List<AntiqueType> Types = new List<AntiqueType>();
        public readonly List<Country> Countries = new List<Country>();
        public readonly List<Century> Centuries = new List<Century>();

        public bool Rising => Direction == RumourDirection.Rise;

        /// <summary>Attributes with at least one value named (0–3).</summary>
        public int AttributeCount =>
            (Types.Count > 0 ? 1 : 0) + (Countries.Count > 0 ? 1 : 0) + (Centuries.Count > 0 ? 1 : 0);

        public int TotalValues => Types.Count + Countries.Count + Centuries.Count;

        public bool IsEmpty => TotalValues == 0;

        public RumourClaim() { }

        public RumourClaim(RumourDirection direction) => Direction = direction;

        public RumourClaim Copy()
        {
            var copy = new RumourClaim(Direction);
            copy.Types.AddRange(Types);
            copy.Countries.AddRange(Countries);
            copy.Centuries.AddRange(Centuries);
            return copy;
        }

        // ---------------------------------------------------------------- editing

        public bool Contains(AntiqueType value) => Types.Contains(value);
        public bool Contains(Country value) => Countries.Contains(value);
        public bool Contains(Century value) => Centuries.Contains(value);

        /// <summary>Adds the value if missing, removes it if present. Returns true when it ends up named.</summary>
        public bool Toggle(AntiqueType value) => Toggle(Types, value);
        public bool Toggle(Country value) => Toggle(Countries, value);
        public bool Toggle(Century value) => Toggle(Centuries, value);

        private static bool Toggle<T>(List<T> values, T value)
        {
            if (values.Remove(value)) return false;

            values.Add(value);
            return true;
        }

        public void Clear()
        {
            Types.Clear();
            Countries.Clear();
            Centuries.Clear();
        }

        // ---------------------------------------------------------------- what it covers

        /// <summary>One entry per named value, all in the claim's direction. This is what NPCs read.</summary>
        public List<NewsEventData> ToNewsData()
        {
            bool up = Rising;
            var data = new List<NewsEventData>();

            foreach (var type in Types)
                data.Add(new NewsEventData(EventEffect.TargetScope.AntiqueType, type, Country.Other, Century.Unknown, up));

            foreach (var country in Countries)
                data.Add(new NewsEventData(EventEffect.TargetScope.Country, AntiqueType.Other, country, Century.Unknown, up));

            foreach (var century in Centuries)
                data.Add(new NewsEventData(EventEffect.TargetScope.Century, AntiqueType.Other, Country.Other, century, up));

            return data;
        }

        /// <summary>Every antique definition the claim covers, each once.</summary>
        public List<AntiqueDefinition> AffectedDefinitions()
        {
            var seen = new HashSet<string>();
            var result = new List<AntiqueDefinition>();

            void Add(List<AntiqueDefinition> definitions)
            {
                foreach (var definition in definitions)
                    if (definition != null && seen.Add(definition.Id))
                        result.Add(definition);
            }

            foreach (var type in Types) Add(AntiqueDatabase.GetByType(type));
            foreach (var country in Countries) Add(AntiqueDatabase.GetByCountry(country));
            foreach (var century in Centuries) Add(AntiqueDatabase.GetByCentury(century));

            return result;
        }

        public bool Matches(Antique antique) =>
            antique != null &&
            (Types.Contains(antique.Type) || Countries.Contains(antique.Country) || Centuries.Contains(antique.Century));

        /// <summary>"AntiqueType:Clock", "Country:France", … — for same-scope fatigue.</summary>
        public List<string> ScopeKeys()
        {
            var keys = new List<string>();

            foreach (var type in Types) keys.Add($"AntiqueType:{type}");
            foreach (var country in Countries) keys.Add($"Country:{country}");
            foreach (var century in Centuries) keys.Add($"Century:{century}");

            return keys;
        }

        // ---------------------------------------------------------------- wording

        /// <summary>"clocks and vases, French pieces, XVIII century pieces"</summary>
        public string Summary()
        {
            var parts = new List<string>();

            if (Types.Count > 0) parts.Add(Join(Types.Select(t => NewsVocabulary.Prose(t))));
            if (Countries.Count > 0) parts.Add(Join(Countries.Select(c => NewsVocabulary.Prose(c))));
            if (Centuries.Count > 0) parts.Add(Join(Centuries.Select(c => NewsVocabulary.Prose(c))));

            return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
        }

        private static string Join(IEnumerable<string> values) =>
            NewsWording.JoinNice(values.ToList());

        // ---------------------------------------------------------------- save / load

        public RumourClaimState Capture() => new RumourClaimState
        {
            Direction = Direction,
            Types = new List<AntiqueType>(Types),
            Countries = new List<Country>(Countries),
            Centuries = new List<Century>(Centuries)
        };
    }

    /// <summary>A claim in a save file.</summary>
    public class RumourClaimState
    {
        public RumourDirection Direction;
        public List<AntiqueType> Types = new List<AntiqueType>();
        public List<Country> Countries = new List<Country>();
        public List<Century> Centuries = new List<Century>();

        public RumourClaim Restore()
        {
            var claim = new RumourClaim(Direction);
            if (Types != null) claim.Types.AddRange(Types);
            if (Countries != null) claim.Countries.AddRange(Countries);
            if (Centuries != null) claim.Centuries.AddRange(Centuries);
            return claim;
        }
    }
}
