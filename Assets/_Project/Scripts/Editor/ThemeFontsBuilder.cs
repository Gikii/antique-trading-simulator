using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Typography from the mockup, all in EB Garamond:
    ///  - headings (view titles, panel headers written in capitals, navigation): SemiBold, shown in
    ///    capitals via TMP's UPPERCASE style, normal letter spacing
    ///  - body text: Medium; texts that were bold: SemiBold (instead of TMP's synthetic bold)
    /// Creates static TMP font assets from Art/Fonts/*.ttf (Latin + € and the symbols the UI uses,
    /// LiberationSans as fallback for anything else), assigns them to every text in Main.unity and
    /// in the UI prefabs, and makes EB Garamond the TMP default for texts created from code.
    /// EB Garamond is optically ~12% smaller than LiberationSans/Cinzel at the same point size, so
    /// texts switched to it are scaled up once (texts already in EB Garamond are not scaled again).
    /// Safe to run more than once. (Cinzel files stay in Art/Fonts for a future logo, unused here.)
    /// </summary>
    public static class ThemeFontsBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Apply Theme Fonts";

        private const string FontsFolder = "Assets/_Project/Art/Fonts/";
        private const string PrefabsFolder = "Assets/_Project/Prefabs/UI";
        private const string LiberationPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";

        private const string BodyMedium = "EBGaramond-Medium";
        private const string BodySemiBold = "EBGaramond-SemiBold";

        /// <summary>Size multiplier for texts switched to EB Garamond (matches LiberationSans' visual size).</summary>
        private const float BodyScale = 1.12f;
        /// <summary>Minimum size of texts created from code (history rows), matching the scaled body text.</summary>
        private const float CodeTextMinSize = 17f;
        /// <summary>Texts named like titles are headings only from this size (small "Title" labels stay body text).</summary>
        private const float TitleMinSize = 18f;
        /// <summary>
        /// ...and only when short ("Market", "Company"); longer "Title" texts are content
        /// (an event name, a company name) and stay normal wrapping text.
        /// </summary>
        private const int TitleMaxLength = 16;
        /// <summary>View titles ("MARKET", "COMPANY") are capped at this size, as in the mockup.</summary>
        private const float ViewTitleMaxSize = 28f;

        /// <summary>Card descriptions (century, condition, seller, shipping) stay on one line at this size.</summary>
        private const float CardDescriptionSize = 11.5f;
        /// <summary>TMP line spacing (em/100) for wrapping body text: 1.3 em lines become ~1.22 em.</summary>
        private const float BodyLineSpacing = -8f;
        /// <summary>Size given to texts whose stored size was invalid (≤ 0).</summary>
        private const float RepairedFontSize = 18f;
        /// <summary>Smallest size auto-sizing may shrink wrapping body text to.</summary>
        private const float MinAutoSize = 9f;
        private const string ListingCardPrefab = "Assets/_Project/Prefabs/UI/ListingCard.prefab";
        private static readonly string[] CardDescriptionNames = { "CenturyText", "ConditionText", "SellerText", "ShippingText" };

        // Atlas: big enough for Latin + extras at a sampling size that stays sharp up to ~48 pt.
        private const int SamplingPointSize = 56;
        private const int AtlasPadding = 6;
        private const int AtlasSize = 1024;

        /// <summary>Latin-1 letters, Polish letters and the symbols the UI prints (€ • · — … arrows, quotes).</summary>
        private const string ExtraCharacters =
            "€•·—–…↑↓→←°×±§©®™‘’“”„«»" +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÑÒÓÔÕÖØÙÚÛÜÝßàáâãäåæçèéêëìíîïñòóôõöøùúûüýÿ" +
            "ĄąĆćĘęŁłŃńŚśŹźŻż";

        private enum FontRole { Body, BodyBold, Heading }

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);

            var bodyMedium = LoadOrCreateFontAsset(BodyMedium, liberation);
            var bodySemiBold = LoadOrCreateFontAsset(BodySemiBold, liberation);

            if (bodyMedium == null || bodySemiBold == null)
            {
                Debug.LogError($"[ThemeFontsBuilder] Font files missing in {FontsFolder} — nothing applied.");
                return;
            }

            var fonts = new Dictionary<FontRole, TMP_FontAsset>
            {
                { FontRole.Body, bodyMedium },
                { FontRole.BodyBold, bodySemiBold },
                { FontRole.Heading, bodySemiBold },
            };
            // Texts already in EB Garamond are not scaled again.
            var themeFonts = new HashSet<TMP_FontAsset> { bodyMedium, bodySemiBold };

            SetTmpDefaultFont(bodyMedium);

            int texts = ApplyToScene(fonts, themeFonts);
            texts += ApplyToPrefabs(fonts, themeFonts);

            AssetDatabase.SaveAssets();
            Debug.Log($"[ThemeFontsBuilder] Fonts applied to {texts} text(s). Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // Font assets
        // ------------------------------------------------------------------

        private static TMP_FontAsset LoadOrCreateFontAsset(string fileName, TMP_FontAsset fallback)
        {
            string assetPath = FontsFolder + fileName + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
                return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontsFolder + fileName + ".ttf");
            if (font == null)
            {
                Debug.LogError($"[ThemeFontsBuilder] {FontsFolder}{fileName}.ttf not found.");
                return null;
            }

            var asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding,
                GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
            {
                Debug.LogError($"[ThemeFontsBuilder] TMP could not create a font asset from {fileName}.ttf.");
                return null;
            }

            asset.name = fileName + " SDF";
            if (!asset.TryAddCharacters(CharacterSet(), out string missing) && !string.IsNullOrEmpty(missing))
                Debug.Log($"[ThemeFontsBuilder] {fileName}: not in the font (falls back to LiberationSans): {missing}");

            // Baked atlas: no glyphs are added at runtime, so the asset doesn't change while playing.
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            if (fallback != null)
                asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };

            AssetDatabase.CreateAsset(asset, assetPath);
            for (int i = 0; i < asset.atlasTextures.Length; i++)
            {
                var texture = asset.atlasTextures[i];
                if (texture == null) continue;
                texture.name = $"{fileName} Atlas {i}";
                AssetDatabase.AddObjectToAsset(texture, asset);
            }
            asset.material.name = fileName + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log($"[ThemeFontsBuilder] Created {assetPath}.", asset);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }

        private static string CharacterSet()
        {
            var sb = new StringBuilder();
            for (char c = ' '; c <= '~'; c++)
                sb.Append(c);
            sb.Append(ExtraCharacters);
            return sb.ToString();
        }

        /// <summary>Texts created from code (tooltips, history rows) use TMP's default font.</summary>
        private static void SetTmpDefaultFont(TMP_FontAsset font)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning($"[ThemeFontsBuilder] {TmpSettingsPath} not found — texts created from code keep LiberationSans.");
                return;
            }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop != null)
            {
                prop.objectReferenceValue = font;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ------------------------------------------------------------------
        // Assigning fonts
        // ------------------------------------------------------------------

        private static int ApplyToScene(Dictionary<FontRole, TMP_FontAsset> fonts, HashSet<TMP_FontAsset> themeFonts)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[ThemeFontsBuilder] Open the {SceneName} scene to update its texts (prefabs are still updated).");
                return 0;
            }

            Transform canvas = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CanvasName) { canvas = go.transform; break; }

            if (canvas == null)
            {
                Debug.LogError($"[ThemeFontsBuilder] No root '{CanvasName}' in {SceneName}.");
                return 0;
            }

            int count = 0;
            Transform nav = canvas.Find("MainNavigation");
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                // Texts inside prefab instances are handled in the prefab itself.
                if (PrefabUtility.IsPartOfPrefabInstance(text)) continue;

                bool isNavigation = nav != null && text.transform.IsChildOf(nav);
                Undo.RecordObject(text, "Apply theme font");
                var layout = text.GetComponent<LayoutElement>();
                if (layout != null) Undo.RecordObject(layout, "Fit theme font");
                bool changed = Apply(text, fonts, themeFonts, isNavigation);
                changed |= FitToFont(text, RoleOf(text, isNavigation, fonts[FontRole.BodyBold]));
                if (changed)
                    count++;
            }

            // History rows are created from code with their own size field.
            foreach (var history in canvas.GetComponentsInChildren<TransactionHistoryUI>(true))
            {
                var so = new SerializedObject(history);
                var size = so.FindProperty("rowFontSize");
                if (size != null && size.floatValue < CodeTextMinSize)
                {
                    size.floatValue = CodeTextMinSize;
                    so.ApplyModifiedProperties();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return count;
        }

        private static int ApplyToPrefabs(Dictionary<FontRole, TMP_FontAsset> fonts, HashSet<TMP_FontAsset> themeFonts)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changed = 0;
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        // Nested prefab instances are updated through their own prefab asset.
                        if (PrefabUtility.IsPartOfPrefabInstance(text)) continue;
                        bool textChanged = Apply(text, fonts, themeFonts, false);
                        textChanged |= FitToFont(text, RoleOf(text, false, fonts[FontRole.BodyBold]));
                        if (textChanged)
                            changed++;
                    }

                    if (path == ListingCardPrefab)
                        changed += FitListingCard(root);

                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        count += changed;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return count;
        }

        private static bool Apply(TMP_Text text, Dictionary<FontRole, TMP_FontAsset> fonts, HashSet<TMP_FontAsset> themeFonts, bool isNavigation)
        {
            // TMP stores -99 as "size not set yet" in some texts; scaling that value (an earlier run
            // of this tool did) gives a huge mirrored text. Repair both cases with a sane size.
            bool repaired = false;
            if (text.fontSize <= 0f)
            {
                Debug.LogWarning($"[ThemeFontsBuilder] '{text.name}' had font size {text.fontSize} — set to {RepairedFontSize}.", text);
                text.fontSize = RepairedFontSize;
                repaired = true;
            }

            var role = RoleOf(text, isNavigation, fonts[FontRole.BodyBold]);
            var target = fonts[role];

            var style = text.fontStyle;

            // Texts an earlier, broader heading rule turned into one-line capitals (e.g. a long
            // event name): back to normal wrapping text.
            if (role != FontRole.Heading && (style & FontStyles.UpperCase) != 0 && themeFonts.Contains(text.font))
                text.textWrappingMode = TextWrappingModes.Normal;
            // The SemiBold face replaces TMP's synthetic bold.
            if (role != FontRole.Body)
                style &= ~FontStyles.Bold;
            if (role == FontRole.Heading)
                style |= FontStyles.UpperCase;
            else
                style &= ~FontStyles.UpperCase; // undo a heading style from an earlier, broader rule

            bool spacingOk = role != FontRole.Heading || Mathf.Approximately(text.characterSpacing, 0f);
            if (text.font == target && text.fontStyle == style && spacingOk)
            {
                if (repaired) EditorUtility.SetDirty(text);
                return repaired;
            }

            if (!themeFonts.Contains(text.font))
            {
                text.fontSize = Mathf.Round(text.fontSize * BodyScale * 2f) / 2f;
                if (text.enableAutoSizing)
                {
                    text.fontSizeMin = Mathf.Round(text.fontSizeMin * BodyScale * 2f) / 2f;
                    text.fontSizeMax = Mathf.Round(text.fontSizeMax * BodyScale * 2f) / 2f;
                }
            }

            text.font = target;
            text.fontSharedMaterial = target.material;
            text.fontStyle = style;
            if (role == FontRole.Heading)
                text.characterSpacing = 0f;

            EditorUtility.SetDirty(text);
            return true;
        }

        /// <summary>
        /// EB Garamond lines are ~27% taller than the LiberationSans lines the layout was made for,
        /// and TMP hides a whole line that doesn't fit when overflow is Truncate. So: never truncate,
        /// keep headings on one line, and give single-line texts at least one line of height
        /// (and headings with a fixed width enough width for their capitals).
        /// </summary>
        private static bool FitToFont(TMP_Text text, FontRole role)
        {
            bool changed = false;

            if (text.overflowMode == TextOverflowModes.Truncate)
            {
                text.overflowMode = TextOverflowModes.Overflow;
                changed = true;
            }

            if (role == FontRole.Heading && text.textWrappingMode != TextWrappingModes.NoWrap)
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                changed = true;
            }

            if (role == FontRole.Heading && IsViewTitle(text))
                changed |= FitViewTitle(text);

            var layout = text.GetComponent<LayoutElement>();
            if (layout != null && text.textWrappingMode == TextWrappingModes.NoWrap)
            {
                float line = Mathf.Ceil(ThemeControlsBuilder.LineHeight(text));
                if (layout.preferredHeight > 0f && layout.preferredHeight < line) { layout.preferredHeight = line; changed = true; }
                // A minimum stops a crowded layout group from squeezing the line to nothing
                // (with Ellipsis TMP then hides it completely — e.g. the HUD date under the day).
                if (layout.minHeight < line) { layout.minHeight = line; changed = true; }

                if (role == FontRole.Heading && layout.preferredWidth > 0f && !string.IsNullOrEmpty(text.text))
                {
                    float width = Mathf.Ceil(text.GetPreferredValues(text.text.ToUpperInvariant()).x + 4f);
                    if (layout.preferredWidth < width) { layout.preferredWidth = width; changed = true; }
                }

                if (changed) EditorUtility.SetDirty(layout);
            }

            // EB Garamond's default line height (1.3 em) is loose for UI paragraphs; tighten it a bit
            // so two-line descriptions fit where LiberationSans lines did.
            if (role != FontRole.Heading && text.textWrappingMode == TextWrappingModes.Normal
                && Mathf.Approximately(text.lineSpacing, 0f))
            {
                text.lineSpacing = BodyLineSpacing;
                changed = true;
            }

            // Wrapping body text in a fixed-height box: shrink (down to 75%) instead of spilling
            // over the next element when a long text meets EB Garamond's taller lines.
            if (role != FontRole.Heading && text.textWrappingMode == TextWrappingModes.Normal && !text.enableAutoSizing
                && layout != null && layout.preferredHeight > 0f && text.GetComponent<ContentSizeFitter>() == null)
            {
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = Mathf.Max(MinAutoSize, Mathf.Round(text.fontSize * 0.75f * 2f) / 2f);
                text.enableAutoSizing = true;
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(text);
            return changed;
        }

        /// <summary>The big title in a view's title row (Canvas/Views/…/TitleRow/TitleText).</summary>
        private static bool IsViewTitle(TMP_Text text) =>
            text.name == "TitleText" && text.transform.parent != null && text.transform.parent.name.EndsWith("TitleRow");

        /// <summary>
        /// Caps the title size and makes its row (and the header around it) tall enough for one line,
        /// so the title no longer takes the height of the tab bar below it.
        /// </summary>
        private static bool FitViewTitle(TMP_Text text)
        {
            bool changed = false;
            if (text.fontSize > ViewTitleMaxSize)
            {
                text.fontSize = ViewTitleMaxSize;
                changed = true;
            }

            float line = Mathf.Ceil(ThemeControlsBuilder.LineHeight(text));

            // An earlier run may have reserved the height of the bigger, uncapped title.
            var titleLayout = text.GetComponent<LayoutElement>();
            if (titleLayout != null && (titleLayout.minHeight != line || titleLayout.preferredHeight > line))
            {
                Undo.RecordObject(titleLayout, "Fit view title");
                titleLayout.minHeight = line;
                if (titleLayout.preferredHeight > 0f) titleLayout.preferredHeight = line;
                EditorUtility.SetDirty(titleLayout);
                changed = true;
            }

            var row = text.transform.parent;
            var rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null || rowLayout.preferredHeight <= 0f || rowLayout.preferredHeight >= line)
                return changed;

            float growth = line - rowLayout.preferredHeight;
            Undo.RecordObject(rowLayout, "Fit title row");
            rowLayout.preferredHeight = line;
            EditorUtility.SetDirty(rowLayout);

            // The header that stacks the title row and the tabs has a fixed height too.
            var header = row.parent;
            var headerLayout = header != null ? header.GetComponent<LayoutElement>() : null;
            if (headerLayout != null && headerLayout.preferredHeight > 0f && header.GetComponent<VerticalLayoutGroup>() != null)
            {
                Undo.RecordObject(headerLayout, "Fit view header");
                headerLayout.preferredHeight += growth;
                EditorUtility.SetDirty(headerLayout);
            }
            return true;
        }

        /// <summary>
        /// Market card: four short description lines at a fixed small size, on one line each (they
        /// fit the narrow column at this size), and the name gets two full lines of its new font.
        /// </summary>
        private static int FitListingCard(GameObject root)
        {
            int changed = 0;
            TMP_Text nameText = null;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "NameText") { nameText = text; continue; }
                if (System.Array.IndexOf(CardDescriptionNames, text.name) < 0) continue;

                text.fontSize = CardDescriptionSize;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                EditorUtility.SetDirty(text);
                changed++;
            }

            if (nameText != null)
            {
                ThemeControlsBuilder.FitInfoColumn(nameText);
                changed++;
            }
            return changed;
        }

        private static FontRole RoleOf(TMP_Text text, bool isNavigation, TMP_FontAsset semiBold)
        {
            bool heading = isNavigation
                || (text.name.Contains("Title") && text.fontSize >= TitleMinSize
                    && (text.text ?? "").Length <= TitleMaxLength)
                || IsUppercaseHeader(text.text);

            if (heading)
                return FontRole.Heading;

            // After a first run the bold flag is gone and the text already uses SemiBold.
            bool bold = (text.fontStyle & FontStyles.Bold) != 0 || text.font == semiBold;
            return bold ? FontRole.BodyBold : FontRole.Body;
        }

        /// <summary>"TRANSACTION HISTORY", "WAREHOUSE" — at least four letters, none of them lowercase.</summary>
        private static bool IsUppercaseHeader(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            int letters = 0;
            foreach (char c in value)
            {
                if (char.IsLower(c)) return false;
                if (char.IsLetter(c)) letters++;
            }
            return letters >= 4;
        }
    }
}
