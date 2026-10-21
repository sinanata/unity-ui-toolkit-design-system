using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Controls
{
    /// <summary>
    /// A circular progress indicator: a full-circle track with an arc drawn over it, painted with
    /// <see cref="Painter2D"/>. The arc covers <see cref="value"/> (0..1) of the circle, clockwise from
    /// <see cref="startAngle"/>.
    ///
    /// Size comes from USS (width and height; the ring fits the smaller side of the content box), the
    /// stroke from the <c>thickness</c> attribute, and both colours from custom properties, so a theme
    /// or a state class can recolour a ring without code:
    /// <code>
    /// .ds-progress-ring { --ds-ring-color: var(--color-primary); --ds-ring-track-color: var(--color-surface-elev); }
    /// </code>
    /// Competitive.uss ships those defaults plus <c>--warning</c>, <c>--danger</c> and a
    /// <c>--tier-*</c> modifier per rank tier. Without the stylesheet the arc falls back to the
    /// element's text colour and the track to that colour at 20%, so a ring is never invisible.
    ///
    /// Cheap by construction: no Update, no schedule, no per-element loop. Setting a property marks the
    /// element for repaint only when the number actually changed, and the mesh is rebuilt on that
    /// repaint without a managed allocation. A countdown that sets <see cref="value"/> every frame
    /// redraws one ring per frame and nothing else; one that sets it once a second, once a second.
    ///
    /// The ring centres its children, which is how a countdown puts its number inside it.
    /// <code>
    /// &lt;DesignSystem.Runtime.Controls.DsProgressRing value="0.6" thickness="6" class="ds-progress-ring--danger"/&gt;
    /// </code>
    /// </summary>
    [UxmlElement]
    public partial class DsProgressRing : VisualElement
    {
        /// <summary>Added to every instance; Competitive.uss keys the default size and colours off it.</summary>
        public static readonly string ussClassName = "ds-progress-ring";

        private static readonly CustomStyleProperty<Color> s_RingColor = new("--ds-ring-color");
        private static readonly CustomStyleProperty<Color> s_TrackColor = new("--ds-ring-track-color");

        // Below this the ring would be a blot rather than a ring; above half the size it would be a disc.
        private const float MinThickness = 0.5f;
        private const float FallbackTrackAlpha = 0.2f;

        private float m_Value;
        private float m_Thickness = 4f;
        private float m_StartAngle = -90f;
        private Color m_RingColor;
        private Color m_TrackColor;
        private bool m_HasRingColor;
        private bool m_HasTrackColor;

        public DsProgressRing()
        {
            AddToClassList(ussClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        /// <summary>How much of the ring is filled, 0..1. Values outside clamp; NaN reads as 0.</summary>
        [UxmlAttribute]
        public float value
        {
            get => m_Value;
            set
            {
                var v = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
                if (v == m_Value) return;
                m_Value = v;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Stroke width in pixels, shared by the track and the arc. Never more than half the ring.</summary>
        [UxmlAttribute]
        public float thickness
        {
            get => m_Thickness;
            set
            {
                var t = float.IsNaN(value) ? MinThickness : Mathf.Max(MinThickness, value);
                if (t == m_Thickness) return;
                m_Thickness = t;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Where the arc begins, in degrees clockwise from 3 o'clock. Default -90: 12 o'clock.</summary>
        [UxmlAttribute("start-angle")]
        public float startAngle
        {
            get => m_StartAngle;
            set
            {
                var a = float.IsNaN(value) ? -90f : value;
                if (a == m_StartAngle) return;
                m_StartAngle = a;
                MarkDirtyRepaint();
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            var style = evt.customStyle;
            var hasRing = style.TryGetValue(s_RingColor, out var ring);
            var hasTrack = style.TryGetValue(s_TrackColor, out var track);
            if (hasRing == m_HasRingColor && hasTrack == m_HasTrackColor && ring == m_RingColor && track == m_TrackColor)
                return;

            m_HasRingColor = hasRing;
            m_RingColor = ring;
            m_HasTrackColor = hasTrack;
            m_TrackColor = track;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            var size = Mathf.Min(rect.width, rect.height);
            if (!(size > 0f)) return;   // also rejects NaN before the first layout

            var stroke = Mathf.Min(m_Thickness, size * 0.5f);
            var radius = (size - stroke) * 0.5f;
            var center = rect.center;

            var ring = m_HasRingColor ? m_RingColor : resolvedStyle.color;
            var track = m_HasTrackColor ? m_TrackColor : new Color(ring.r, ring.g, ring.b, ring.a * FallbackTrackAlpha);

            var painter = mgc.painter2D;
            painter.lineWidth = stroke;
            painter.lineJoin = LineJoin.Round;

            if (track.a > 0f)
            {
                painter.strokeColor = track;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.ClosePath();
                painter.Stroke();
            }

            if (m_Value <= 0f || ring.a <= 0f) return;

            painter.strokeColor = ring;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(m_StartAngle), Angle.Degrees(m_StartAngle + 360f * m_Value));
            if (m_Value >= 1f)
            {
                // A full ring has no ends: close it so the seam joins instead of butting.
                painter.lineCap = LineCap.Butt;
                painter.ClosePath();
            }
            else
            {
                // Round ends read as the head of something moving, which is what a draining timer is.
                painter.lineCap = LineCap.Round;
            }
            painter.Stroke();
        }
    }
}
