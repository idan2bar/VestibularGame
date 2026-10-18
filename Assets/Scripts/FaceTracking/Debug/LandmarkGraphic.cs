using UnityEngine;
using UnityEngine.UI;

namespace VestibularGame.FaceTracking.DebugUI
{
    /// <summary>
    /// Draws face landmarks as small quads inside its RectTransform. Input is normalized, unmirrored camera
    /// coordinates (x right, y DOWN); the camera aspect is preserved by letterboxing.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LandmarkGraphic : MaskableGraphic
    {
        Vector3[] points;
        int count;
        bool mirror;
        float aspect = 4f / 3f;

        bool[] highlight = new bool[FaceFrame.LandmarkCount];
        float pointSize = 3f;
        float highlightSize = 7f;
        Color pointColor = new Color(0.75f, 0.9f, 1f, 0.9f);
        Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>
        /// Sets the points to draw. The array is referenced, not copied; call again (or <see cref="Refresh"/>)
        /// after changing its contents. A <paramref name="count"/> of 0 clears the display.
        /// </summary>
        public void SetPoints(Vector3[] normalizedLandmarks, int count, bool mirror)
        {
            points = normalizedLandmarks;
            this.count = normalizedLandmarks == null ? 0 : Mathf.Clamp(count, 0, normalizedLandmarks.Length);
            this.mirror = mirror;
            SetVerticesDirty();
        }

        /// <summary>Rebuilds the mesh after the referenced points array was modified in place.</summary>
        public void Refresh() => SetVerticesDirty();

        /// <summary>Width / height of the camera image (default 4:3).</summary>
        public void SetAspect(float widthOverHeight)
        {
            if (widthOverHeight <= 0.01f || Mathf.Approximately(aspect, widthOverHeight)) return;
            aspect = widthOverHeight;
            SetVerticesDirty();
        }

        /// <summary>Marks the given landmark indices to be drawn with the highlight color and size (replaces any previous set).</summary>
        public void SetHighlightIndices(int[] indices)
        {
            System.Array.Clear(highlight, 0, highlight.Length);
            if (indices != null)
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    int idx = indices[i];
                    if (idx < 0) continue;
                    if (idx >= highlight.Length) System.Array.Resize(ref highlight, idx + 1);
                    highlight[idx] = true;
                }
            }
            SetVerticesDirty();
        }

        /// <summary>Additionally highlights the inclusive index range [first, last].</summary>
        public void AddHighlightRange(int first, int last)
        {
            if (first < 0 || last < first) return;
            if (last >= highlight.Length) System.Array.Resize(ref highlight, last + 1);
            for (int i = first; i <= last; i++) highlight[i] = true;
            SetVerticesDirty();
        }

        /// <summary>Sets colors and quad sizes (in RectTransform units). The Graphic's own <c>color</c> tints both.</summary>
        public void SetStyle(Color pointColor, float pointSize, Color highlightColor, float highlightSize)
        {
            this.pointColor = pointColor;
            this.pointSize = pointSize;
            this.highlightColor = highlightColor;
            this.highlightSize = highlightSize;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || count == 0) return;

            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;

            float w, h;
            if (r.width / r.height > aspect) { h = r.height; w = h * aspect; }
            else { w = r.width; h = w / aspect; }
            Vector2 c = r.center;

            // Two passes so highlighted points are drawn on top.
            for (int pass = 0; pass < 2; pass++)
            {
                bool hl = pass == 1;
                Color32 col = (hl ? highlightColor : pointColor) * color;
                float half = (hl ? highlightSize : pointSize) * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    bool isHl = i < highlight.Length && highlight[i];
                    if (isHl != hl) continue;

                    Vector3 p = points[i];
                    float x = mirror ? 0.5f - p.x : p.x - 0.5f;
                    float cx = c.x + x * w;
                    float cy = c.y + (0.5f - p.y) * h;

                    int v = vh.currentVertCount;
                    vh.AddVert(new Vector3(cx - half, cy - half), col, Vector2.zero);
                    vh.AddVert(new Vector3(cx - half, cy + half), col, Vector2.zero);
                    vh.AddVert(new Vector3(cx + half, cy + half), col, Vector2.zero);
                    vh.AddVert(new Vector3(cx + half, cy - half), col, Vector2.zero);
                    vh.AddTriangle(v, v + 1, v + 2);
                    vh.AddTriangle(v + 2, v + 3, v);
                }
            }
        }
    }
}
