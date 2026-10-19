using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.News;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// All UI icons in one asset (engraved gold emblems, Art/UI/Icons/ico_*.png).
    /// Filled by Tools → Antique Trading Simulator → UI → Apply Icons, which matches each field to
    /// its file by name (CollectionValue → ico_collection_value.png, badges → ico_rep_1..5 / ico_cred_1..5).
    /// Static icons are placed in the scene by that tool; icons that depend on game state
    /// (upgrade, news type, reputation level) are looked up here at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "UIIcons", menuName = "AntiqueTradingSimulator/UI Icons")]
    public class UIIcons : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "UIIcons";

        [Header("Navigation")]
        public Sprite Market;
        public Sprite Collection;
        public Sprite Auction;
        public Sprite Contracts;
        public Sprite News;
        public Sprite Company;
        public Sprite Warehouse;
        public Sprite Settings;

        [Header("Stats")]
        public Sprite Cash;
        public Sprite Wealth;
        public Sprite CollectionValue;
        public Sprite Reputation;
        public Sprite Credibility;
        public Sprite MarketShare;
        public Sprite ContractsFulfilled;
        public Sprite Calendar;
        public Sprite Income;
        public Sprite Expenses;
        public Sprite Profit;
        public Sprite Deadline;

        [Header("Company upgrades")]
        public Sprite UpgWarehouse;
        public Sprite UpgSecurity;
        public Sprite UpgInformation;
        public Sprite UpgLogistics;
        public Sprite UpgExperts;
        public Sprite UpgConservators;
        public Sprite UpgStaff;
        public Sprite InfoAccess;

        [Header("Actions and modal headers")]
        public Sprite Buy;
        public Sprite Sell;
        public Sprite List;
        public Sprite Restore;
        public Sprite Fulfil;
        public Sprite CreateEvent;
        public Sprite Info;
        public Sprite Warning;

        [Header("News and event details")]
        public Sprite Category;
        public Sprite Period;
        public Sprite Region;
        public Sprite Date;
        public Sprite NewsOfficial;
        public Sprite NewsRumour;
        public Sprite NewsLeaked;
        public Sprite NewsFake;
        public Sprite Duration;

        [Header("Level badges (lowest level first)")]
        public Sprite[] ReputationBadges = new Sprite[0];
        public Sprite[] CredibilityBadges = new Sprite[0];

        private static UIIcons _cached;

        public Sprite ForUpgrade(CompanyUpgradeType type) => type switch
        {
            CompanyUpgradeType.Warehouse => UpgWarehouse,
            CompanyUpgradeType.Security => UpgSecurity,
            CompanyUpgradeType.InformationNetwork => UpgInformation,
            CompanyUpgradeType.Logistics => UpgLogistics,
            CompanyUpgradeType.Experts => UpgExperts,
            CompanyUpgradeType.Conservators => UpgConservators,
            CompanyUpgradeType.Staff => UpgStaff,
            _ => null
        };

        public Sprite ForNewsType(NewsType type) => type switch
        {
            NewsType.Official => NewsOfficial,
            NewsType.Rumor => NewsRumour,
            NewsType.Leak => NewsLeaked,
            NewsType.Fake => NewsFake,
            _ => null
        };

        /// <summary>
        /// Badge for level <paramref name="index"/> of <paramref name="count"/> levels. The level ladders
        /// and the badge sets can have different lengths, so the lowest level always gets the first
        /// badge, the highest the last, and the ones in between are spread evenly.
        /// </summary>
        public Sprite ForTier(ReputationKind kind, int index, int count)
        {
            var badges = kind == ReputationKind.Reputation ? ReputationBadges : CredibilityBadges;
            if (badges == null || badges.Length == 0)
                return null;
            if (count <= 1)
                return badges[0];

            int badge = Mathf.RoundToInt(index * (badges.Length - 1f) / (count - 1f));
            return badges[Mathf.Clamp(badge, 0, badges.Length - 1)];
        }

        /// <summary>Shows a sprite as an icon: untinted, not stretched. Does nothing without a sprite.</summary>
        public static void Show(Image image, Sprite sprite)
        {
            if (image == null || sprite == null)
                return;

            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
        }

        /// <summary>The icons from Resources, or null if they haven't been set up yet.</summary>
        public static UIIcons Load()
        {
            if (_cached == null)
                _cached = Resources.Load<UIIcons>(ResourcePath);
            return _cached;
        }
    }
}
