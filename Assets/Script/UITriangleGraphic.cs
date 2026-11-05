using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A triangle UI graphic drawn in code (no sprite/prefab needed).
/// Apex = the vertical center of the left edge (rect.xMin) (= the start point); base = the full height at the right edge (rect.xMax).
/// Placing it with pivot (0, 0.5) + sizeDelta(length, base width) produces a wedge/triangle where "the apex is the start point and the base points in the direction of travel (toward the button)".
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UITriangleGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        Vector2 apex = new Vector2(r.xMin, r.center.y);     // apex (start point = fingerprint center)
        Vector2 baseTop = new Vector2(r.xMax, r.yMax);      // top end of the base (toward the button)
        Vector2 baseBottom = new Vector2(r.xMax, r.yMin);   // bottom end of the base (toward the button)

        Color32 c = color;
        vh.AddVert(apex, c, new Vector2(0f, 0.5f));
        vh.AddVert(baseTop, c, new Vector2(1f, 1f));
        vh.AddVert(baseBottom, c, new Vector2(1f, 0f));
        vh.AddTriangle(0, 1, 2);
    }
}
