#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// One look within a family: the three palette colors every shader reads, plus the
    /// family's own parameter vector.
    ///
    /// The three colors are a contract the foundation depends on — <c>DsFxPalette</c>
    /// derives the whole tone ladder and the readable ink from them, so they must mean
    /// what their names say:
    ///   ColA — the deep tone. Its ALPHA is the master material alpha (a translucent
    ///          family like a glass pane sets it below 1; opaque families leave it at 1).
    ///   ColB — the light tone / highlight.
    ///   ColC — the accent: glints, embers, emissive, rim light.
    /// <see cref="Params"/> is yours: four floats, meaning whatever your shader says it
    /// means. Document the four slots in your shader's header comment.
    /// </summary>
    public sealed class DsFxVariant
    {
        public readonly string Name;
        public readonly Color ColA;
        public readonly Color ColB;
        public readonly Color ColC;
        public readonly Vector4 Params;

        public DsFxVariant(string name, Color colA, Color colB, Color colC, Vector4 parameters)
        {
            Name = name;
            ColA = colA;
            ColB = colB;
            ColC = colC;
            Params = parameters;
        }
    }

    /// <summary>
    /// A material family: its shader, its variant book, and the handful of traits the
    /// pipeline needs in order to treat it correctly.
    ///
    /// This type is what keeps the pipeline open. The renderer knows nothing about what
    /// any given material IS — it asks the family for a shader, pushes the variant's
    /// colors into it, and consults the traits below wherever a decision genuinely
    /// depends on the material's nature. Anything a family needs beyond these traits
    /// belongs in the shader, driven by <see cref="DsFxVariant.Params"/>.
    ///
    /// Register once at startup and the whole marker vocabulary works for you:
    /// <code>
    /// [RuntimeInitializeOnLoadMethod]
    /// static void Register() => DsFxRegistry.Register(new DsFxFamily(
    ///     "moss", "Hidden/MyGame/Moss", "MyGame/Shaders/Moss", variants) { HighFigure = true });
    /// </code>
    /// From then on <c>ds-fx-moss--variant</c> in UXML, and DsFxTheme.Apply, both work.
    /// </summary>
    public sealed class DsFxFamily
    {
        /// <summary>The marker head: "moss" answers to <c>ds-fx-moss</c>. Lowercase, no spaces.</summary>
        public readonly string Name;

        /// <summary>Shader.Find name. Tried first.</summary>
        public readonly string ShaderName;

        /// <summary>
        /// Resources.Load path, used when Shader.Find comes back empty. Keep the shader under a
        /// Resources folder and name it here: a player build strips shaders nothing references,
        /// and a material built at runtime references nothing at build time.
        /// </summary>
        public readonly string ShaderResource;

        /// <summary>The variant book. The FIRST entry is the family default (a bare
        /// <c>ds-fx-moss</c>, or an unknown variant name, resolves to it).</summary>
        public readonly DsFxVariant[] Variants;

        // ---- traits: the only per-family knowledge the pipeline itself acts on ----

        /// <summary>Seconds the entrance animation runs. Fast, punchy materials want less;
        /// something that flows or settles wants more.</summary>
        public float InDuration = 0.9f;

        /// <summary>
        /// How loud this family's MULTIPLIED structure stays on a panel (reaches the shader as
        /// <c>f.texGain</c>). 0.85 suits a family whose figure is carried by its palette — grain,
        /// brushing — because such figure sits under body text without fighting it. A family whose
        /// pattern is ADDITIVE light (caustics, scanlines, rain) must drop to roughly 0.22, or
        /// paragraphs on its panels become unreadable. Controls and wells always get full voice;
        /// this is a panel-only damping.
        /// </summary>
        public float PanelTextureGain = 0.85f;

        /// <summary>
        /// How hard <c>ds-fx-adopt</c> pulls ColC toward a control's own semantic color. The
        /// default 0.40 keeps an accent that belongs to the material (a gold glint stays gold on
        /// red-stained wood, which is right). Raise it toward 0.80 only when the accent IS the
        /// family's identity — a neon tube, an emissive rim — because at 0.40 a danger button's
        /// tube stays the family hue instead of going red.
        /// </summary>
        public float AccentAdoptStrength = 0.40f;

        /// <summary>
        /// True when this family's surface is BUSY — cathedral grain, brushed metal, caustics,
        /// falling glyphs. Body ink riding a busy surface needs a thick counter-toned halo; the
        /// thin default drowns in the figure. Set it honestly: it costs nothing and unreadable
        /// text is the one failure users cannot work around.
        /// </summary>
        public bool HighFigure;

        public DsFxFamily(string name, string shaderName, string shaderResource, DsFxVariant[] variants)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A family needs a name — it is the ds-fx-<name> marker head.", nameof(name));
            if (variants == null || variants.Length == 0)
                throw new ArgumentException($"Family '{name}' needs at least one variant; the first is its default.", nameof(variants));

            Name = name;
            ShaderName = shaderName;
            ShaderResource = shaderResource;
            Variants = variants;
        }

        /// <summary>Variant by name. Null, empty or unknown falls back to the family default.</summary>
        public DsFxVariant Find(string variantName)
        {
            if (!string.IsNullOrEmpty(variantName))
            {
                foreach (var v in Variants)
                    if (string.Equals(v.Name, variantName, StringComparison.OrdinalIgnoreCase))
                        return v;
            }
            return Variants[0];
        }
    }

    /// <summary>
    /// The open family table. The pipeline resolves every <c>ds-fx-&lt;name&gt;</c> marker through
    /// here, so a family exists exactly as far as it has been registered — there is no built-in
    /// list to edit and nothing to fork.
    ///
    /// Register from <c>[RuntimeInitializeOnLoadMethod]</c> so families are present before any
    /// tree attaches. Registering the same name twice REPLACES the earlier entry, which is what
    /// you want when overriding a shipped family with your own.
    /// </summary>
    public static class DsFxRegistry
    {
        private static readonly Dictionary<string, DsFxFamily> ByName =
            new Dictionary<string, DsFxFamily>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<DsFxFamily> Ordered = new List<DsFxFamily>();

        /// <summary>Every registered family, in registration order.</summary>
        public static IReadOnlyList<DsFxFamily> All => Ordered;

        public static void Register(DsFxFamily family)
        {
            if (family == null)
                throw new ArgumentNullException(nameof(family));
            if (ByName.TryGetValue(family.Name, out var existing))
                Ordered.Remove(existing);
            ByName[family.Name] = family;
            Ordered.Add(family);
        }

        public static bool TryGet(string name, out DsFxFamily family)
            => ByName.TryGetValue(name ?? string.Empty, out family);

        /// <summary>The family, or null when nothing answers to that name.</summary>
        public static DsFxFamily Get(string name)
            => ByName.TryGetValue(name ?? string.Empty, out var f) ? f : null;
    }
}
#endif
