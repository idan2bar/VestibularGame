using UnityEngine;
using UnityEngine.UI;

namespace VestibularGame.FaceTracking.DebugUI
{
    /// <summary>
    /// Scatter plot of up to <see cref="SeriesCount"/> series of recent (x, y) samples in degrees, drawn inside its RectTransform.
    /// Axes span ±<see cref="RangeX"/> on x and ±<see cref="RangeY"/> on y, with (-RangeX, -RangeY) at the bottom-left. The
    /// horizontal y = 0 axis is drawn bright (it is the ideal line), with faint guides at y = ±<see cref="GuideY"/>. Each series is a
    /// fixed-capacity ring buffer; points are drawn as small quads whose opacity fades with age. Series whose colour has zero alpha
    /// are stored and fitted but not drawn. The background is expected to be a separate Image behind this graphic.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class ScatterPlotGraphic : MaskableGraphic
    {
        public const int SeriesCount = 4;
        public const int Capacity = 150;
        public const int MinFitCount = 20;
        public const float MinXVariance = 4f;

        /// <summary>Horizontal guide lines are drawn at y = ±GuideY degrees (the on-centre threshold).</summary>
        public const float GuideY = 5f;

        /// <summary>Axes span ±RangeX degrees on x.</summary>
        public float RangeX = 30f;

        /// <summary>Axes span ±RangeY degrees on y.</summary>
        public float RangeY = 15f;

        /// <summary>Per-series colour; a series with zero alpha is not drawn.</summary>
        public Color[] SeriesColors =
        {
            new Color(1f, 0.6f, 0.15f, 1f),
            new Color(0.2f, 0.85f, 1f, 1f),
            Color.clear,
            Color.clear
        };

        static readonly Color VerticalAxisColor = new Color(0.6f, 0.6f, 0.6f, 0.4f);
        static readonly Color ZeroLineColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.15f);

        const float PointSize = 6f;
        const float LineThickness = 1.5f;
        const float ZeroLineThickness = 3f;
        const float MinPointAlpha = 0.15f;

        readonly float[][] xs = { new float[Capacity], new float[Capacity], new float[Capacity], new float[Capacity] };
        readonly float[][] ys = { new float[Capacity], new float[Capacity], new float[Capacity], new float[Capacity] };
        readonly int[] heads = new int[SeriesCount];   // next write index
        readonly int[] counts = new int[SeriesCount];

        /// <summary>Adds a point to the series (0..3), overwriting the oldest once full.</summary>
        public void AddPoint(int series, float x, float y)
        {
            if ((uint)series >= SeriesCount) return;
            if (float.IsNaN(x) || float.IsNaN(y)) return;

            int h = heads[series];
            xs[series][h] = x;
            ys[series][h] = y;
            heads[series] = (h + 1) % Capacity;
            if (counts[series] < Capacity) counts[series]++;
            SetVerticesDirty();
        }

        /// <summary>Removes all points from all series.</summary>
        public void Clear()
        {
            for (int s = 0; s < SeriesCount; s++)
            {
                heads[s] = 0;
                counts[s] = 0;
            }
            SetVerticesDirty();
        }

        /// <summary>
        /// Root mean square of y over the series' buffer, i.e. how far the points sit from the y = 0 ideal line.
        /// Returns false when the series is empty.
        /// </summary>
        public bool TryGetStats(int series, out float rms, out int count)
        {
            rms = 0f;
            count = 0;
            if ((uint)series >= SeriesCount) return false;

            int n = counts[series];
            count = n;
            if (n == 0) return false;

            var sy = ys[series];
            double sum = 0;
            for (int i = 0; i < n; i++) sum += (double)sy[i] * sy[i];
            rms = (float)System.Math.Sqrt(sum / n);
            return true;
        }

        /// <summary>
        /// Least-squares fit y = slope * x + b over the series' buffer. Returns false until there are enough points
        /// (<see cref="MinFitCount"/>) with enough spread in x (variance above <see cref="MinXVariance"/> deg²).
        /// </summary>
        public bool TryGetFit(int series, out float slope, out float r2, out int count)
        {
            slope = 0f;
            r2 = 0f;
            count = 0;
            if ((uint)series >= SeriesCount) return false;

            int n = counts[series];
            count = n;
            if (n < MinFitCount) return false;

            var sx = xs[series];
            var sy = ys[series];
            double meanX = 0, meanY = 0;
            for (int i = 0; i < n; i++)
            {
                meanX += sx[i];
                meanY += sy[i];
            }
            meanX /= n;
            meanY /= n;

            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = sx[i] - meanX;
                double dy = sy[i] - meanY;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            if (sxx / n <= MinXVariance) return false;

            slope = (float)(sxy / sxx);
            r2 = syy > 1e-9 ? (float)(sxy * sxy / (sxx * syy)) : 0f;
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f || RangeX <= 0f || RangeY <= 0f) return;

            // Faint guides at the on-centre threshold, then the vertical axis, then the bright y = 0 ideal line on top.
            if (GuideY < RangeY)
            {
                float yUp = Map(r, 0f, GuideY).y;
                float yDown = Map(r, 0f, -GuideY).y;
                AddLine(vh, new Vector2(r.xMin, yUp), new Vector2(r.xMax, yUp), LineThickness, GuideColor * color);
                AddLine(vh, new Vector2(r.xMin, yDown), new Vector2(r.xMax, yDown), LineThickness, GuideColor * color);
            }

            Vector2 center = Map(r, 0f, 0f);
            AddLine(vh, new Vector2(center.x, r.yMin), new Vector2(center.x, r.yMax), LineThickness, VerticalAxisColor * color);
            AddLine(vh, new Vector2(r.xMin, center.y), new Vector2(r.xMax, center.y), ZeroLineThickness, ZeroLineColor * color);

            // Points, oldest first so newer ones draw on top.
            float half = PointSize * 0.5f;
            int colorCount = SeriesColors != null ? SeriesColors.Length : 0;
            for (int s = 0; s < SeriesCount && s < colorCount; s++)
            {
                int n = counts[s];
                if (n == 0) continue;
                if (SeriesColors[s].a <= 0f) continue;

                Color baseColor = SeriesColors[s] * color;
                int start = n < Capacity ? 0 : heads[s];
                for (int i = 0; i < n; i++)
                {
                    int idx = (start + i) % Capacity;
                    float age01 = n > 1 ? (float)i / (n - 1) : 1f; // 0 = oldest, 1 = newest
                    Color c = baseColor;
                    c.a *= Mathf.Lerp(MinPointAlpha, 1f, age01);

                    Vector2 p = Map(r, xs[s][idx], ys[s][idx]);
                    AddQuad(vh, p.x - half, p.y - half, p.x + half, p.y + half, c);
                }
            }
        }

        Vector2 Map(Rect r, float x, float y)
        {
            float nx = Mathf.Clamp(x, -RangeX, RangeX) / RangeX * 0.5f + 0.5f;
            float ny = Mathf.Clamp(y, -RangeY, RangeY) / RangeY * 0.5f + 0.5f;
            return new Vector2(r.xMin + nx * r.width, r.yMin + ny * r.height);
        }

        static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 col)
        {
            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), col, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), col, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v + 2, v + 3, v);
        }

        static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color32 col)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return;
            Vector2 n = new Vector2(-d.y, d.x) * (thickness * 0.5f / len);

            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x - n.x, a.y - n.y), col, Vector2.zero);
            vh.AddVert(new Vector3(a.x + n.x, a.y + n.y), col, Vector2.zero);
            vh.AddVert(new Vector3(b.x + n.x, b.y + n.y), col, Vector2.zero);
            vh.AddVert(new Vector3(b.x - n.x, b.y - n.y), col, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v + 2, v + 3, v);
        }
    }
}
