using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SemiCircleButtonLayout : NumberButtonSpawnerBase
{
    public enum ArcPreset
    {
        [UnityEngine.InspectorName("Top Semicircle")]
        TopSemicircle,
        [UnityEngine.InspectorName("Bottom Semicircle")]
        BottomSemicircle,
        [UnityEngine.InspectorName("Right Semicircle")]
        RightSemicircle,
        [UnityEngine.InspectorName("Left Semicircle")]
        LeftSemicircle,
        [UnityEngine.InspectorName("Custom Input")]
        Custom
    }

    /// <summary>
    /// SpanEndpoints: the first/last buttons sit at the two ends of the arc. With N=8 and 315°, the spacing equals 360°/8, so it looks the same as a regular octagon.<br/>
    /// InsetMargins: distributes evenly inside the arc with a sweep/(virtualN+1) margin at each end -> the empty 45° section differs from a circle divided into 8 equal parts.
    /// </summary>
    public enum ArcSpacingMode
    {
        [UnityEngine.InspectorName("Even Inside Arc + End Margins")]
        InsetMargins,
        [UnityEngine.InspectorName("First/Last = Arc Ends")]
        SpanEndpoints,
    }

    [Header("References")]
    [Tooltip("Number buttons pre-placed in the scene (in order, starting from 1)")]
    [SerializeField] private List<RectTransform> preplacedButtons = new();
    [SerializeField] private RectTransform centerButton;
    [Tooltip("If empty, uses Center Button. Specify to base the layout on a specific child (e.g. a prompt Image) only")]
    [SerializeField] private RectTransform arcCenterRect;
    [Tooltip("If on, uses the center of the union of world bounds of all RectTransforms under Center (or Arc Center Rect) as the arc origin")]
    [SerializeField] private bool arcCenterIncludeChildren;
    [SerializeField] private RectTransform buttonPrefab;
    [SerializeField] private Transform spawnParent;

    [Header("Bake")]
    [SerializeField] private int maxPreplacedCount = 10;

    [Header("Direct Endpoint Angles (directly set angles for buttons 1 and 8)")]
    [Tooltip("If on, uses the First/Last angles below to 'directly' set the positions of slot 1 (first slot) and the reference-count-th slot (e.g. slot 8). arcDegrees, startAngleDeg, orientArcByGapCenter and arcSpacingMode are ignored. Angle reference: 0=right(+X), 90=up(+Y), 180=left, 270=down.")]
    [SerializeField] private bool useDirectEndpointAngles = false;

    // ┌─────────────────────────────────────────────────────────────────────┐
    // │  ★ Change the angles directly here (code values always take priority, not baked into the scene) ★   │
    // │  Angle reference: 0=right(+X), 90=up, 180=left, 270=down                     │
    // │  If clockwise=true, the angle decreases from First(button 1) to Last(button 8), going around the top  │
    // └─────────────────────────────────────────────────────────────────────┘
    private const float FirstButtonAngleDeg = 239f;  // Position of button 1 (increase the value to move it right -> down/right)
    private const float LastButtonAngleDeg  = 26f;   // Position of button 8 (increase the value to move it up -> up/left)

    [Header("Arc Layout")]
    [SerializeField] private ArcPreset arcPreset = ArcPreset.Custom;
    [SerializeField] private float arcDegrees = 315f;
    [SerializeField] private float startAngleDeg = 180f;
    [Tooltip("Overall layout rotation (degrees). The empty section rotates along with it.")]
    [SerializeField] private float globalAngleOffsetDeg = 0f;
    [Tooltip("If on, instead of Start Angle, aligns the center of the empty angle (360-Arc Degrees) to the Gap Center Angle")]
    [SerializeField] private bool orientArcByGapCenter = false;
    [Tooltip("Direction toward the center of the empty section (degrees). Cos/Sin reference: 0=right(+X), 90=up(+Y). Only used when Orient is on")]
    [SerializeField] private float gapCenterAngleDeg = 270f;
    [SerializeField] private bool clockwise = true;
    [SerializeField] private float fixedRadiusPx = 450f;
    [SerializeField] private Vector2 centerOffsetPx = Vector2.zero;
    [Tooltip("If true, keeps the angular spacing of the reference count (angleSpacingReferenceCount) even when N decreases")]
    [SerializeField] private bool keepAngleSpacingWhenCountChanges = true;
    [Tooltip("Reference count for angular spacing (8 recommended when using an N=8, 315° arc)")]
    [SerializeField] private int angleSpacingReferenceCount = 8;
    [SerializeField] private ArcSpacingMode arcSpacingMode = ArcSpacingMode.InsetMargins;
    [Tooltip("If on, takes the arc origin as the world center of the Spawn Parent Rect (+ Center Offset) instead of main, and aligns main to the circumcenter when done")]
    [SerializeField] private bool layoutFromSpawnParentRectCenter = false;
    [Tooltip("Right after layout, moves main (visual center) to the circumcenter of the circle formed by the orbital buttons")]
    [SerializeField] private bool alignMainToCircumcenterAfterLayout = false;
    [Tooltip("If on, right after layout, computes the center (circumcenter) of the circle formed by the actually placed buttons and translates the whole circle so its center matches the main (center button) visual center. Forces all buttons to be equidistant from the center button regardless of the cause. main does not move.")]
    [SerializeField] private bool recenterButtonsOnCenterButton = true;
    [Tooltip("Whether to recompute button positions on runtime SetCount. If off, uses the pre-placed coordinates as-is and only toggles active/inactive")]
    [SerializeField] private bool relayoutOnSetCount = false;

    [Header("Link")]
    [SerializeField] private HickExperiment experiment;      // ExperimentManager

    private readonly List<RectTransform> activeButtons = new();
    private static readonly Vector3[] WorldCornersScratch = new Vector3[4];
    private int n;

    public override void SetCount(int newN)
    {
        n = Mathf.Max(0, newN);
        ActivateForCount();
    }

    public override int GetMaxSupportedCount()
    {
        if (preplacedButtons == null || preplacedButtons.Count == 0) return 0;

        int count = 0;
        int maxSlot = 0;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;
            if (centerButton != null && button == centerButton) continue;
            count++;
            var nb = button.GetComponent<NumberButton>();
            if (nb != null) maxSlot = Mathf.Max(maxSlot, nb.Number);
        }
        return maxSlot > 0 ? maxSlot : count;
    }

    [ContextMenu("Apply Active Buttons")]
    public void ActivateForCount()
    {
        if (preplacedButtons.Count == 0)
        {
            Debug.LogError("[SemiCircleButtonLayout] preplacedButtons is empty. Connect the buttons directly in the Inspector, in order starting from 1.");
            return;
        }
        if (preplacedButtons.Count < n)
            Debug.LogError($"[SemiCircleButtonLayout] Not enough buttons: available={preplacedButtons.Count}, requestedN={n}");

        activeButtons.Clear();
        int skippedCenterCount = 0;
        int peripheralIndex = 0;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;
            if (centerButton != null && button == centerButton)
            {
                if (button.gameObject.activeSelf)
                    button.gameObject.SetActive(true);
                skippedCenterCount++;
                continue;
            }

            var nb = button.GetComponent<NumberButton>();
            int slot = nb != null ? nb.Number : ++peripheralIndex;
            bool shouldShow = slot >= 1 && slot <= n;
            if (button.gameObject.activeSelf != shouldShow)
                button.gameObject.SetActive(shouldShow);
            if (shouldShow)
                activeButtons.Add(button);
        }
        if (skippedCenterCount > 0)
            Debug.LogWarning($"[SemiCircleButtonLayout] centerButton was included in preplacedButtons and was automatically excluded: {skippedCenterCount}");

        activeButtons.Sort((a, b) =>
        {
            int na = a.GetComponent<NumberButton>()?.Number ?? 0;
            int nb = b.GetComponent<NumberButton>()?.Number ?? 0;
            int c = na.CompareTo(nb);
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });

        int activeCount = activeButtons.Count;
        if (activeCount < n)
            Debug.LogWarning($"[SemiCircleButtonLayout] Requested N={n}, but there are only {activeCount} active buttons with logicalNumber<=N.");
        
        if (relayoutOnSetCount)
        {
            Transform parent = spawnParent;
            if (parent == null && preplacedButtons.Count > 0 && preplacedButtons[0] != null)
                parent = preplacedButtons[0].parent;
            ApplyArcLayout(activeButtons, parent);
        }

        if (experiment != null)
            experiment.RegisterNumberButtons(activeButtons);
        else
            Debug.LogError("[SemiCircleButtonLayout] experiment(HickExperiment) reference required");
    }

    [ContextMenu("Bake Preplaced Buttons (Max Count)")]
    private void BakePreplacedButtons()
    {
        if (buttonPrefab == null)
        {
            Debug.LogError("[SemiCircleButtonLayout] buttonPrefab is empty");
            return;
        }

        Transform parent = spawnParent != null ? spawnParent : transform;
        ClearPreplacedButtons();
        preplacedButtons = new List<RectTransform>();

        int count = Mathf.Max(0, maxPreplacedCount);
        for (int i = 0; i < count; i++)
        {
            RectTransform clone = Instantiate(buttonPrefab, parent);
            clone.name = $"{buttonPrefab.name}_{i + 1}";
            clone.gameObject.SetActive(true);
            preplacedButtons.Add(clone);
        }

        // Prevent the just-created RectTransform world coordinates from remaining 0 in the editor
        Canvas.ForceUpdateCanvases();
        ApplyArcLayout(preplacedButtons, parent);
    }

    [ContextMenu("Align Main To Arc Circumcenter (based on current button positions, no layout)")]
    private void ContextAlignMainToArcCircumcenterOnly()
    {
        Transform parent = spawnParent != null ? spawnParent : transform;
        AlignCenterButtonToCircumcenterOfTargets(preplacedButtons, parent);
        Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
        if (!Application.isPlaying && centerButton != null)
            EditorUtility.SetDirty(centerButton.gameObject);
#endif
    }

    [ContextMenu("Clear Preplaced Buttons")]
    private void ClearPreplacedButtons()
    {
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;
            if (Application.isPlaying) Destroy(button.gameObject);
            else DestroyImmediate(button.gameObject);
        }
        preplacedButtons.Clear();
    }

    private void ApplyArcLayout(List<RectTransform> targets, Transform layoutParent)
    {
        int count = targets.Count;
        if (count == 0) return;

        float sweep;
        float startAngle;
        bool cw;
        if (arcPreset == ArcPreset.Custom)
        {
            sweep = Mathf.Clamp(arcDegrees, 0f, 360f);
            startAngle = startAngleDeg;
            cw = clockwise;
            if (orientArcByGapCenter && sweep < 360f - 0.001f)
            {
                float gapSize = 360f - sweep;
                // The arc is the complementary arc of the gap [C-g/2, C+g/2] -> one end sweeps clockwise from C+g/2
                startAngle = WrapDeg360(gapCenterAngleDeg + gapSize * 0.5f);
            }
        }
        else
        {
            GetPresetAngles(arcPreset, out startAngle, out sweep, out cw);
        }

        int virtualN = keepAngleSpacingWhenCountChanges
            ? Mathf.Max(1, Mathf.Max(count, angleSpacingReferenceCount))
            : Mathf.Max(1, count);

        float step;
        bool useInset = arcSpacingMode == ArcSpacingMode.InsetMargins;
        if (useInset)
            step = sweep / (virtualN + 1);
        else
        {
            int spacingCount = Mathf.Max(2, virtualN);
            step = sweep / (spacingCount - 1);
        }

        startAngle += globalAngleOffsetDeg;

        // ── Direct angle mode: evenly divide, in the clockwise direction, between button 1=firstButtonAngleDeg and the reference-count-th button (e.g. button 8)=lastButtonAngleDeg ──
        // slotAngleDelta = angular increment per slot (with sign). Angle of button i = firstButtonAngleDeg + slotAngleDelta * i.
        bool directAngles = useDirectEndpointAngles;
        float slotAngleDelta = 0f;
        if (directAngles)
        {
            int refSlots = keepAngleSpacingWhenCountChanges
                ? Mathf.Max(2, Mathf.Max(count, angleSpacingReferenceCount))
                : Mathf.Max(2, count);
            // If clockwise, the angle decreases (-) moving First->Last, otherwise increases (+)
            float span = clockwise
                ? Mathf.Repeat(FirstButtonAngleDeg - LastButtonAngleDeg, 360f)   // Amount to subtract (positive)
                : Mathf.Repeat(LastButtonAngleDeg - FirstButtonAngleDeg, 360f);
            slotAngleDelta = (clockwise ? -1f : 1f) * (span / (refSlots - 1));
        }

        RectTransform parentRt = layoutParent as RectTransform;
        Canvas canvas = parentRt != null ? parentRt.GetComponentInParent<Canvas>() : null;
        Camera canvasCam = GetCanvasCameraForUi(parentRt);
        Transform planeRef = canvas != null ? canvas.transform : layoutParent;
        Vector3 ringCenterWorld;
        if (layoutFromSpawnParentRectCenter && layoutParent is RectTransform lpCenter)
        {
            ringCenterWorld = GetRectTransformWorldRectCenter(lpCenter);
            ringCenterWorld += lpCenter.TransformVector(new Vector3(centerOffsetPx.x, centerOffsetPx.y, 0f));
        }
        else
            ringCenterWorld = GetArcRingCenterWorld(layoutParent);

        for (int i = 0; i < count; i++)
        {
            RectTransform target = targets[i];
            if (target == null) continue;
            if (centerButton != null && target == centerButton) continue;

            float angle;
            if (directAngles)
            {
                angle = FirstButtonAngleDeg + slotAngleDelta * i;
            }
            else
            {
                float along = useInset ? step * (i + 1) : step * i;
                angle = startAngle + (cw ? -1f : 1f) * along;
            }
            float rad = angle * Mathf.Deg2Rad;
            Vector2 plane = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * fixedRadiusPx;
            Vector3 wDelta = planeRef != null
                ? planeRef.TransformVector(new Vector3(plane.x, plane.y, 0f))
                : new Vector3(plane.x, plane.y, 0f);
            Vector3 wPos = ringCenterWorld + wDelta;

            if (parentRt != null)
            {
                if (TryWorldPointToAnchoredPosition(parentRt, wPos, canvasCam, out Vector2 ap))
                    target.anchoredPosition = ap;
                else
                {
                    Vector3 loc = parentRt.InverseTransformPoint(wPos);
                    target.anchoredPosition = new Vector2(loc.x, loc.y);
                }
            }
            else
            {
                Vector3 loc = layoutParent.InverseTransformPoint(wPos);
                target.anchoredPosition = new Vector2(loc.x, loc.y);
            }
        }

        // Align the center of the actually placed button circle to the main center, guaranteeing equidistance regardless of the cause (main stays fixed)
        if (recenterButtonsOnCenterButton && centerButton != null)
        {
            Canvas.ForceUpdateCanvases();
            RecenterTargetsOnCenterButton(targets);
        }

        if (alignMainToCircumcenterAfterLayout && centerButton != null)
        {
            AlignCenterButtonToCircumcenterOfTargets(targets, layoutParent);
            Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(centerButton.gameObject);
#endif
        }
    }

    /// <summary>Computes the center (circumcenter) of the circle formed by the actually placed orbital buttons and translates all buttons so that center lands on the main visual center. Each button's relative position (angle/radius) is preserved, so the even spacing stays the same and only the center is aligned to main.</summary>
    private void RecenterTargetsOnCenterButton(List<RectTransform> targets)
    {
        if (centerButton == null || targets == null) return;

        var pts = new List<Vector2>(targets.Count);
        var moved = new List<RectTransform>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform t = targets[i];
            if (t == null || t == centerButton as RectTransform) continue;
            Vector3 w = GetRectTransformWorldRectCenter(t);
            pts.Add(new Vector2(w.x, w.y));
            moved.Add(t);
        }
        if (pts.Count < 3) return; // Circumcenter computation needs at least 3 (non-collinear) points

        // Compute the circumcenter stably using 3 points as far apart as possible
        int mid = pts.Count / 2;
        Vector2 circumcenter = Circumcenter2D(pts[0], pts[mid], pts[^1]);
        if (float.IsNaN(circumcenter.x) || float.IsNaN(circumcenter.y)) return;

        Vector3 mainCenter = GetRectTransformWorldRectCenter(centerButton);
        Vector3 delta = new Vector3(mainCenter.x - circumcenter.x, mainCenter.y - circumcenter.y, 0f);
        if (delta.sqrMagnitude < 1e-6f) return;

        for (int i = 0; i < moved.Count; i++)
            moved[i].position += delta;
    }

    /// <summary>Computes the circumcenter from the orbital buttons' world coordinates (xy), then moves main's visual center to that point.</summary>
    private void AlignCenterButtonToCircumcenterOfTargets(List<RectTransform> targets, Transform layoutParent)
    {
        if (centerButton == null || targets == null) return;

        var pts = new List<Vector2>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform t = targets[i];
            if (t == null) continue;
            if (t == centerButton as RectTransform) continue;
            Vector3 w = GetRectTransformWorldRectCenter(t);
            pts.Add(new Vector2(w.x, w.y));
        }
        if (pts.Count < 2) return;

        Vector2 cXy;
        if (pts.Count == 2)
            cXy = (pts[0] + pts[1]) * 0.5f;
        else
        {
            int mid = pts.Count / 2;
            cXy = Circumcenter2D(pts[0], pts[mid], pts[^1]);
        }

        Vector3 curCenter = centerButton is RectTransform mrt
            ? GetRectTransformWorldRectCenter(mrt)
            : centerButton.position;
        Vector3 targetCenter = new Vector3(cXy.x, cXy.y, curCenter.z);
        centerButton.position += targetCenter - curCenter;
    }

    private static Vector2 Circumcenter2D(Vector2 a, Vector2 b, Vector2 c)
    {
        float d = 2f * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
        if (Mathf.Abs(d) < 1e-4f)
            return (a + b + c) / 3f;
        float a2 = a.sqrMagnitude;
        float b2 = b.sqrMagnitude;
        float c2 = c.sqrMagnitude;
        float ux = (a2 * (b.y - c.y) + b2 * (c.y - a.y) + c2 * (a.y - b.y)) / d;
        float uy = (a2 * (c.x - b.x) + b2 * (a.x - c.x) + c2 * (b.x - a.x)) / d;
        return new Vector2(ux, uy);
    }

    private Vector3 GetArcRingCenterWorld(Transform layoutParent)
    {
        RectTransform src = arcCenterRect != null ? arcCenterRect : centerButton as RectTransform;
        Vector3 wc;
        if (src != null)
        {
            wc = arcCenterIncludeChildren
                ? GetRectTreeWorldBoundsCenter(src)
                : GetRectTransformWorldRectCenter(src);
        }
        else if (centerButton != null)
            wc = centerButton.position;
        else
            wc = Vector3.zero;

        if (layoutParent is RectTransform lp)
            wc += lp.TransformVector(new Vector3(centerOffsetPx.x, centerOffsetPx.y, 0f));
        else if (layoutParent != null)
            wc += layoutParent.TransformVector(new Vector3(centerOffsetPx.x, centerOffsetPx.y, 0f));
        return wc;
    }

    /// <summary>Accounts for anchor/pivot/width/height/rotation/scale: the average of the rect's four corner world coordinates = the center of the rectangle as seen on screen.</summary>
    private static Vector3 GetRectTransformWorldRectCenter(RectTransform rt)
    {
        if (rt == null) return Vector3.zero;
        rt.GetWorldCorners(WorldCornersScratch);
        return (WorldCornersScratch[0] + WorldCornersScratch[1] + WorldCornersScratch[2] + WorldCornersScratch[3]) * 0.25f;
    }

    /// <summary>Center of the AABB combining the world corners of this Rect and all descendant Rects (useful when a child image is larger).</summary>
    private static Vector3 GetRectTreeWorldBoundsCenter(RectTransform root)
    {
        if (root == null) return Vector3.zero;
        var rects = root.GetComponentsInChildren<RectTransform>(true);
        Bounds b = default;
        bool first = true;
        for (int r = 0; r < rects.Length; r++)
        {
            rects[r].GetWorldCorners(WorldCornersScratch);
            for (int i = 0; i < 4; i++)
            {
                if (first)
                {
                    b = new Bounds(WorldCornersScratch[i], Vector3.zero);
                    first = false;
                }
                else
                    b.Encapsulate(WorldCornersScratch[i]);
            }
        }
        return first ? Vector3.zero : b.center;
    }

    private static Camera GetCanvasCameraForUi(RectTransform anyUnderCanvas)
    {
        if (anyUnderCanvas == null) return null;
        Canvas c = anyUnderCanvas.GetComponentInParent<Canvas>();
        if (c == null) return null;
        if (c.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return c.worldCamera != null ? c.worldCamera : Camera.main;
    }

    private static bool TryWorldPointToAnchoredPosition(RectTransform parent, Vector3 worldPoint, Camera cam, out Vector2 anchored)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, worldPoint);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out anchored);
    }

    private static void GetPresetAngles(ArcPreset preset, out float startDeg, out float sweep, out bool isClockwise)
    {
        switch (preset)
        {
            case ArcPreset.TopSemicircle:
                startDeg = 180f;
                sweep = 180f;
                isClockwise = true;
                break;
            case ArcPreset.BottomSemicircle:
                startDeg = 0f;
                sweep = 180f;
                isClockwise = false;
                break;
            case ArcPreset.RightSemicircle:
                startDeg = 270f;
                sweep = 180f;
                isClockwise = false;
                break;
            case ArcPreset.LeftSemicircle:
                startDeg = 90f;
                sweep = 180f;
                isClockwise = false;
                break;
            default:
                startDeg = 180f;
                sweep = 180f;
                isClockwise = true;
                break;
        }
    }

    private static float WrapDeg360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }
}