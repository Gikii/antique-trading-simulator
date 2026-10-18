using AntiqueTradingSimulator.Art;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// A picture slot made of three layers: the slot's own Image is the backdrop, then a soft
    /// floor shadow, then the antique itself (transparent PNG, kept at 3:4 by an
    /// AspectRatioFitter so the shadow sits under the object's base). Backdrop and shadow
    /// come from AntiqueArtCatalog, so restyling every slot means changing one asset.
    /// Built by Tools → Antique Trading Simulator → Art → Build Antique Image Slots.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public class AntiqueImageSlot : MonoBehaviour
    {
        /// <summary>Width / height of the artwork files (768x1024).</summary>
        public const float ArtworkAspect = 3f / 4f;

        [SerializeField] private Image background;
        [SerializeField] private Image shadow;
        [SerializeField] private Image artwork;
        [Tooltip("Off for contract pictures, which are emblems rather than objects standing on a surface.")]
        [SerializeField] private bool showShadow = true;

        private bool _styled;

        private void Awake()
        {
            ApplyCatalogStyle();
        }

        /// <summary>Shows the sprite; null hides the object and its shadow, leaving the backdrop.</summary>
        public void SetSprite(Sprite sprite)
        {
            ApplyCatalogStyle();

            bool hasSprite = sprite != null;

            if (artwork != null)
            {
                artwork.sprite = sprite;
                artwork.preserveAspect = true;
                artwork.enabled = hasSprite;
            }

            if (shadow != null)
                shadow.enabled = hasSprite && showShadow && shadow.sprite != null;
        }

        private void ApplyCatalogStyle()
        {
            if (_styled)
                return;
            _styled = true;

            if (background == null)
                background = GetComponent<Image>();

            var catalog = AntiqueArt.Catalog;
            if (catalog == null)
                return; // no catalog yet — keep the greybox look

            if (background != null)
            {
                // The colour is only a stand-in for a missing backdrop sprite — tinting the
                // (already dark) sprite with it would multiply it down to near black.
                background.sprite = catalog.SlotBackground;
                background.color = catalog.SlotBackground != null ? Color.white : catalog.SlotBackgroundColor;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;
            }

            if (shadow != null)
            {
                shadow.sprite = catalog.FloorShadow;
                shadow.color = new Color(0f, 0f, 0f, catalog.FloorShadowAlpha);
                shadow.enabled = false; // shown once there is an object to stand on it
            }
        }
    }

    /// <summary>
    /// Entry points for the UI scripts. They work on the plain Image field each script already
    /// has: if the slot has been built, the AntiqueImageSlot does the layering; otherwise the
    /// sprite goes straight into the Image (so nothing breaks before the builder runs).
    /// </summary>
    public static class AntiqueImageExtensions
    {
        public static void ShowAntique(this Image target, Antique antique) =>
            target.ShowArtwork(AntiqueArt.For(antique));

        public static void ShowContract(this Image target, ContractRequirement requirement) =>
            target.ShowArtwork(AntiqueArt.ForContract(requirement));

        public static void ShowArtwork(this Image target, Sprite sprite)
        {
            if (target == null)
                return;

            var slot = target.GetComponent<AntiqueImageSlot>();
            if (slot != null)
            {
                slot.SetSprite(sprite);
                return;
            }

            if (sprite == null)
                return; // keep the greybox placeholder colour

            target.sprite = sprite;
            target.color = Color.white;
            target.preserveAspect = true;
        }
    }
}
