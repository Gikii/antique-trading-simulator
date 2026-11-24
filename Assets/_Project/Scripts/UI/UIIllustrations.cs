using System;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Painted illustrations of the Company view (Art/UI/Illustrations/ill_*.png), cropped to their slots.
    /// Filled by Tools → Antique Trading Simulator → UI → Company → Apply Illustrations, which matches
    /// the fields to files by name (CompanyTiers → ill_company_tier_*, sorted; Upgrades →
    /// ill_upgrade_&lt;type&gt;_banner, e.g. ill_upgrade_information_network_banner; emblems → ill_emblem_player and
    /// ill_emblem_&lt;profile name&gt;, e.g. ill_emblem_museum). The pictures that depend
    /// on game state (the company card follows the reputation level) are looked up here at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "UIIllustrations", menuName = "AntiqueTradingSimulator/UI Illustrations")]
    public class UIIllustrations : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "UIIllustrations";

        [Header("Company → Overview")]
        [Tooltip("Company card, one per reputation level (Local Shop → Elite House). A ladder of another " +
                 "length is spread over these: the lowest level gets the first picture, the highest the last.")]
        public Sprite[] CompanyTiers = new Sprite[0];

        [Tooltip("Next major event card.")]
        public Sprite NextEvent;

        [Header("Company → Reputation")]
        public Sprite Reputation;
        public Sprite Credibility;

        [Header("Company → Development")]
        [Tooltip("Wide banner in the upgrade details panel, per upgrade (the cards keep their UIIcons icon). " +
                 "An upgrade without an entry shows no banner.")]
        public UpgradeBanner[] Upgrades = new UpgradeBanner[0];

        [Serializable]
        public struct UpgradeBanner
        {
            public CompanyUpgradeType Type;
            public Sprite Banner;
        }

        [Header("Company → Competition")]
        [Tooltip("Avatar of the player's row in the competition ranking.")]
        public Sprite PlayerEmblem;

        [Tooltip("Avatar of NPC rows, per behaviour profile. An NPC whose profile has no entry shows no avatar.")]
        public ProfileEmblem[] ProfileEmblems = new ProfileEmblem[0];

        [Serializable]
        public struct ProfileEmblem
        {
            public NpcBehaviorProfile Profile;
            public Sprite Emblem;
        }

        private static UIIllustrations _cached;

        /// <summary>Company card for reputation level <paramref name="index"/> of <paramref name="count"/>.</summary>
        public Sprite ForCompanyTier(int index, int count)
        {
            if (CompanyTiers == null || CompanyTiers.Length == 0)
                return null;
            if (count <= 1)
                return CompanyTiers[0];

            int picture = Mathf.RoundToInt(index * (CompanyTiers.Length - 1f) / (count - 1f));
            return CompanyTiers[Mathf.Clamp(picture, 0, CompanyTiers.Length - 1)];
        }

        public Sprite For(ReputationKind kind) =>
            kind == ReputationKind.Reputation ? Reputation : Credibility;

        public Sprite ForUpgradeBanner(CompanyUpgradeType type)
        {
            if (Upgrades != null)
                foreach (var entry in Upgrades)
                    if (entry.Type == type)
                        return entry.Banner;
            return null;
        }

        /// <summary>Avatar for a row of the competition ranking: the player's emblem or the NPC profile's.</summary>
        public Sprite ForCompetitor(bool isPlayer, string profileId)
        {
            if (isPlayer)
                return PlayerEmblem;
            if (string.IsNullOrEmpty(profileId) || ProfileEmblems == null)
                return null;

            foreach (var entry in ProfileEmblems)
                if (entry.Profile != null && entry.Profile.Id == profileId)
                    return entry.Emblem;
            return null;
        }

        /// <summary>
        /// Shows a picture filling its slot, untinted (the files are cropped to the slot's proportions).
        /// Without a sprite the slot is left empty (the Image is disabled, its layout space kept), so a
        /// slot reused for something without a picture — the details panel — never keeps the old one.
        /// </summary>
        public static void Show(Image image, Sprite sprite)
        {
            if (image == null)
                return;

            image.enabled = sprite != null;
            if (sprite == null)
                return;

            // Always set everything, not only when the sprite changes: the placeholder's dark tint
            // must go even if the slot already holds this sprite (the builder's edit-mode preview).
            // The setters skip unchanged values, so calling this every refresh is cheap.
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
        }

        /// <summary>The illustrations from Resources, or null if they haven't been set up yet.</summary>
        public static UIIllustrations Load()
        {
            if (_cached == null)
                _cached = Resources.Load<UIIllustrations>(ResourcePath);
            return _cached;
        }
    }
}
