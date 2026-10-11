using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.Editor
{
    /// <summary>How a scanner button looks.</summary>
    internal enum ButtonStyle
    {
        /// <summary>The one main action of the current step. Filled mint.</summary>
        Primary,

        /// <summary>Choices and helpers: pick a wall, pick a type, undo.</summary>
        Secondary,

        /// <summary>Move on to the next step. Mint text on a tinted fill.</summary>
        Next,

        /// <summary>Small header pills: Restart, Details, the computer chip.</summary>
        Pill
    }

    /// <summary>
    /// The scanner's visual vocabulary for <see cref="ScannerSceneBuilder"/>:
    /// palette, rounded sprites, and factories for cards, rows, labels,
    /// buttons and fields that all size themselves through layout groups.
    ///
    /// <para><b>Nothing here positions anything by pixel.</b> The previous
    /// layout placed every step's controls at hand-picked heights, which
    /// overlapped as soon as a phone's aspect ratio differed from the 1080x1920
    /// reference. Everything built here sits in a layout group and takes its
    /// height from its content.</para>
    /// </summary>
    internal static class ScannerUiKit
    {
        public const string SpriteFolder = "Assets/GhostMap/Scanner/Sprites";

        /// <summary>Corner radius baked into the rounded sprite, in pixels.</summary>
        private const int BakedRadiusPx = 48;

        public static readonly Color Accent = new Color(0.22f, 0.86f, 0.76f);
        public static readonly Color AccentText = new Color(0.03f, 0.13f, 0.12f);
        public static readonly Color CardColor = new Color(0.07f, 0.08f, 0.10f, 0.90f);
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = new Color(0.75f, 0.79f, 0.85f);
        public static readonly Color Fill = new Color(1f, 1f, 1f, 0.12f);

        private static Font font;

        public static Sprite Rounded { get; private set; }

        public static Sprite Circle { get; private set; }

        public static Sprite Ring { get; private set; }

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // -------------------------------------------------------------------
        // Sprites
        // -------------------------------------------------------------------

        /// <summary>
        /// Generates the three sprites the UI is drawn with, once, as committed
        /// PNG assets: a 9-sliced rounded rectangle, a filled circle and a ring.
        /// All white, tinted by <see cref="Image.color"/>, antialiased by
        /// signed distance.
        /// </summary>
        public static void EnsureSprites()
        {
            Rounded = EnsureSprite("Rounded", 128, RoundedRectAlpha, BakedRadiusPx);
            Circle = EnsureSprite("Circle", 128, CircleAlpha, 0);
            Ring = EnsureSprite("Ring", 128, RingAlpha, 0);
        }

        private static Sprite EnsureSprite(string name, int size, Func<float, float, int, float> alpha, int border)
        {
            string path = $"{SpriteFolder}/{name}.png";

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(SpriteFolder);

                var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                var pixels = new Color32[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float a = Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f, size));
                        pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var wantedBorder = new Vector4(border, border, border, border);

            if (importer.textureType != TextureImporterType.Sprite
                || importer.spriteBorder != wantedBorder
                || importer.mipmapEnabled
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = wantedBorder;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite == null)
            {
                throw new InvalidOperationException($"Could not load generated UI sprite at {path}.");
            }

            return sprite;
        }

        private static float RoundedRectAlpha(float px, float py, int size)
        {
            float c = size * 0.5f;
            float r = BakedRadiusPx;
            float dx = Mathf.Max(Mathf.Abs(px - c) - (c - r), 0f);
            float dy = Mathf.Max(Mathf.Abs(py - c) - (c - r), 0f);
            float distance = Mathf.Sqrt((dx * dx) + (dy * dy)) - r;
            return 0.5f - distance;
        }

        private static float CircleAlpha(float px, float py, int size)
        {
            float c = size * 0.5f;
            float distance = Vector2.Distance(new Vector2(px, py), new Vector2(c, c)) - (c - 1f);
            return 0.5f - distance;
        }

        private static float RingAlpha(float px, float py, int size)
        {
            const float stroke = 11f;
            float c = size * 0.5f;
            float middle = c - 1f - (stroke * 0.5f);
            float distance = Mathf.Abs(Vector2.Distance(new Vector2(px, py), new Vector2(c, c)) - middle) - (stroke * 0.5f);
            return 0.5f - distance;
        }

        // -------------------------------------------------------------------
        // Building blocks
        // -------------------------------------------------------------------

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>A rounded, tinted background on <paramref name="go"/>.</summary>
        public static Image Background(GameObject go, Color color, float radius)
        {
            // Not "??": a missing component is Unity's fake null in the Editor.
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }

            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.pixelsPerUnitMultiplier = BakedRadiusPx / Mathf.Max(1f, radius);
            return image;
        }

        /// <summary>
        /// A rounded background for a container that has its own layout group.
        ///
        /// <para>It goes on a child that layout ignores, never on the container
        /// itself: a sliced <see cref="Image"/> reports its sprite borders as a
        /// minimum size, and layout takes the larger of that and the group's
        /// real content size, so a one-line pill came out 96 units tall with
        /// the text stuck to its top.</para>
        /// </summary>
        public static Image PanelBackground(GameObject container, Color color, float radius)
        {
            RectTransform rect = Rect("Background", container.transform);
            rect.SetAsFirstSibling();
            Stretch(rect);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return Background(rect.gameObject, color, radius);
        }

        /// <summary>A rounded card that stacks its children vertically and hugs their height.</summary>
        public static RectTransform Card(string name, Transform parent, Color color, float radius, int padding, float spacing)
        {
            RectTransform rect = Rect(name, parent);
            PanelBackground(rect.gameObject, color, radius);
            Column(rect.gameObject, padding, padding, spacing);
            return rect;
        }

        public static VerticalLayoutGroup Column(GameObject go, int horizontalPadding, int verticalPadding, float spacing)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(horizontalPadding, horizontalPadding, verticalPadding, verticalPadding);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        /// <summary>A horizontal row whose children share its width.</summary>
        public static RectTransform Row(string name, Transform parent, float spacing)
        {
            RectTransform rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static LayoutElement Size(GameObject go, float preferredHeight = -1f, float preferredWidth = -1f, float flexibleWidth = -1f)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }


            if (preferredHeight >= 0f)
            {
                element.minHeight = preferredHeight;
                element.preferredHeight = preferredHeight;
            }

            if (preferredWidth >= 0f)
            {
                element.minWidth = preferredWidth;
                element.preferredWidth = preferredWidth;
            }

            element.flexibleWidth = flexibleWidth;
            return element;
        }

        public static void HugHeight(GameObject go)
        {
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public static Text Label(
            Transform parent,
            string name,
            string text,
            int fontSize,
            Color color,
            FontStyle style = FontStyle.Normal,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.lineSpacing = 1.08f;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(
            Transform parent,
            string name,
            string text,
            ButtonStyle style,
            out Text label,
            float height = -1f)
        {
            RectTransform rect = Rect(name, parent);

            Color fill;
            Color textColor;
            int fontSize;
            float defaultHeight;
            float radius;
            FontStyle fontStyle = FontStyle.Bold;

            switch (style)
            {
                case ButtonStyle.Primary:
                    fill = Accent;
                    textColor = AccentText;
                    fontSize = 40;
                    defaultHeight = 124f;
                    radius = 34f;
                    break;

                case ButtonStyle.Next:
                    fill = new Color(Accent.r, Accent.g, Accent.b, 0.20f);
                    textColor = Accent;
                    fontSize = 34;
                    defaultHeight = 100f;
                    radius = 30f;
                    break;

                case ButtonStyle.Pill:
                    fill = new Color(0.07f, 0.08f, 0.10f, 0.78f);
                    textColor = TextPrimary;
                    fontSize = 28;
                    defaultHeight = 80f;
                    radius = 40f;
                    fontStyle = FontStyle.Normal;
                    break;

                default:
                    fill = Fill;
                    textColor = TextPrimary;
                    fontSize = 32;
                    defaultHeight = 100f;
                    radius = 30f;
                    fontStyle = FontStyle.Normal;
                    break;
            }

            Image background = Background(rect.gameObject, fill, radius);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // A touch must not leave a button looking "selected" afterwards.
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            label = Label(rect, "Label", text, fontSize, textColor, fontStyle, TextAnchor.MiddleCenter);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20;
            label.resizeTextMaxSize = fontSize;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(label.rectTransform, 18f, 18f, 4f, 4f);

            Size(rect.gameObject, height >= 0f ? height : defaultHeight, flexibleWidth: 1f);
            return button;
        }

        public static InputField Field(Transform parent, string name, string placeholderText, float height)
        {
            RectTransform rect = Rect(name, parent);
            Image background = Background(rect.gameObject, Fill, 26f);

            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;

            Text text = Label(rect, "Text", string.Empty, 34, TextPrimary, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Stretch(text.rectTransform, 26f, 26f, 6f, 6f);

            Text placeholder = Label(rect, "Placeholder", placeholderText, 30, new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform, 26f, 26f, 6f, 6f);

            field.textComponent = text;
            field.placeholder = placeholder;
            field.caretColor = Accent;
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            Size(rect.gameObject, height, flexibleWidth: 1f);
            return field;
        }

        /// <summary>A round image, used for the crosshair dot and status dots.</summary>
        public static Image Dot(Transform parent, string name, float diameter, Color color, Sprite sprite = null)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : Circle;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            rect.sizeDelta = new Vector2(diameter, diameter);
            Size(rect.gameObject, diameter, diameter, 0f);
            return image;
        }
    }
}
