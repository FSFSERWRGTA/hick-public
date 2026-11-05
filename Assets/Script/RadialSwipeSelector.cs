// RadialSwipeSelector.cs (snap + line highlight + separated main calls)
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class RadialSwipeSelector : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Refs")]
    [SerializeField] private RectTransform uiRoot;
    [SerializeField] private RectTransform center;
    [SerializeField] private HickExperiment experiment;
    [Tooltip("List of preview target buttons right after editor Bake (in order starting from #1)")]
    [SerializeField] private List<RectTransform> previewTargets = new();

    [Header("Thumb")]
    [SerializeField] private RectTransform thumb;
    [SerializeField] private float thumbRadiusPx = 220f;
    [Tooltip("You must drag at least this distance (px) from the origin before direction is recognized/selected. Larger values prevent selection from slight movements")]
    [SerializeField] private float minDragFromCenterPx = 80f;
    [Tooltip("When off, the thumb does not move (recommended) and only line/button highlighting works")]
    [SerializeField] private bool moveThumbWithSwipe = false;

    [Header("Mode")]
    [SerializeField] private bool confirmOnPointerUp = true;
    
    [Header("Noise Filter")]
    [Tooltip("For this duration (ms) right after pointer down, input is treated as noise and candidate selection is ignored")]
    [SerializeField] private float noiseIgnoreTimeMs = 120f;
    [Tooltip("You must move at least this distance (px) from the pointer-down point before candidate selection begins")]
    [SerializeField] private float noiseMinMovePx = 14f;

    [Header("Visual - Button Highlight")]
    [SerializeField] private float highlightScale = 1.15f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private bool forceButtonTransitionNone = true;

    [Header("Visual - Spoke Highlight")]
    [SerializeField] private RectTransform spokePrefab;     // thin Image (pivot 0,0.5)
    [SerializeField] private Transform spokeParent;         // Canvas recommended
    [SerializeField] private int maxPreplacedSpokeCount = 10;
    [SerializeField] private float spokeThickness = 6f;
    [Tooltip("How far (px) to offset the line's start point from the center (fingerprint button)")]
    [SerializeField] private float spokeStartGapPx = 36f;
    [Tooltip("How much (px) to shorten the line's end just before the target button")]
    [SerializeField] private float spokeEndGapPx = 12f;
    [SerializeField] private Color spokeNormalColor = new Color(0, 0, 0, 0.35f);
    [SerializeField] private Color spokeActiveColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private float spokeActiveThickness = 10f;
    [Tooltip("When on, spokes in the scene/inspector are used first. If none exist, they are auto-generated from the Spoke Prefab (when the Preplaced Only below is off)")]
    [SerializeField] private bool usePreplacedSpokes = true;
    [Tooltip("When on, only preplacedSpokes (the list registered in the Inspector) are used")]
    [SerializeField] private bool useInspectorSpokeListOnly = true;
    [Tooltip("Recompute spoke position/angle on SetTargets. Turn on if the spoke parent differs from Canvas/uiRoot so the lines line up")]
    [SerializeField] private bool relayoutSpokesOnSetTargets = true;
    [Tooltip("When N increases (e.g. 4->8), automatically align the newly activated spokes toward their targets")]
    [SerializeField] private bool relayoutWhenTargetCountIncreases = false;
    [Tooltip("When on, only spokes already present in the hierarchy are used and none are ever created at runtime")]
    [SerializeField] private bool preplacedOnlyNoRuntimeCreate = true;
    [SerializeField] private List<RectTransform> preplacedSpokes = new();
    [SerializeField] private Transform preplacedSpokeParent;
    [Tooltip("Starting length ratio of an active spoke (0~1). Smaller values make the 'spreading from the center' effect stronger")]
    [Range(0f, 1f)]
    [SerializeField] private float activeSpokeMinLengthRatio = 0.15f;

    [Header("Static Lines (Bake) - static lines with no effects")]
    [Tooltip("'Always visible' static lines from the fingerprint button center to each button. Displayed fixed, independent of swipe highlighting (color/length changes).")]
    [SerializeField] private List<RectTransform> staticLines = new();
    [Tooltip("Parent of the static lines. If empty, uses Preplaced Spoke Parent → Spoke Parent → uiRoot in that order")]
    [SerializeField] private Transform staticLineParent;
    [Tooltip("Default color of the static triangle (default #EDEDED)")]
    [SerializeField] private Color staticLineColor = new Color(0.9294118f, 0.9294118f, 0.9294118f, 1f); // #EDEDED
    [SerializeField] private float staticLineThickness = 6f;
    [Tooltip("When on, the triangle's base width matches the diameter of the 'target number button' (base touching the button = button diameter). When off, uses Static Line Thickness.")]
    [SerializeField] private bool staticLineBaseFromCenterDiameter = true;
    [Tooltip("Base width multiplier. 1 = same as button diameter.")]
    [SerializeField] private float staticLineBaseWidthMultiplier = 1f;
    [Tooltip("Distance (px) to offset the base start from the fingerprint center. If 0, the base sits at the exact center of the fingerprint.")]
    [SerializeField] private float staticLineStartGapPx = 0f;
    [Tooltip("Distance (px) to pull back the apex just before the target button.")]
    [SerializeField] private float staticLineEndGapPx = 0f;
    [Tooltip("When on, sends static lines to the back of the parent (first sibling) so they don't cover buttons or the fingerprint.")]
    [SerializeField] private bool staticLinesBehindSiblings = true;
    [Tooltip("When on, moves each triangle to the same parent as its button and places it at the back, so it sits 'below (behind)' the button.")]
    [SerializeField] private bool staticLinesUnderButtons = true;
    [Tooltip("When on, attaches its own Canvas (overrideSorting) to each triangle and forces it behind buttons via sortingOrder. It always goes to the back regardless of sibling order.")]
    [SerializeField] private bool staticLinesUseSortingCanvas = true;
    [Tooltip("sortingOrder of the static triangle's Canvas. Must be lower than the buttons (default 0) to be drawn behind.")]
    [SerializeField] private int staticLineSortingOrder = -1;
    [Tooltip("When on, redraws active static triangles to the current button positions on every SetTargets. Turn on when the spawner auto-places at runtime (relayoutOnSetCount) so the lines follow the buttons.")]
    [SerializeField] private bool relayoutStaticLinesOnSetTargets = false;

    private static readonly Vector3[] s_staticCorners = new Vector3[4];

    private List<(int number, RectTransform rt)> targets = new();
    private readonly List<RectTransform> spokes = new();
    private readonly List<Image> spokeImgs = new();
    private readonly List<float> spokeBaseLen = new();

    private bool spokeRebuildScheduled;
    private bool isDown;
    private int lastAppliedTargetCount = -1;
    private int candidate = -1;
    private RectTransform candidateRt;
    private float pointerDownTime;
    private Vector2 pointerDownLocalPoint;
    private bool pointerDownPointCaptured;

    // Movement onset (lift-off): the first moment the finger leaves the fingerprint origin (monotonic time, seconds). <0 if not captured.
    private double liftRealtime = -1.0;
    private bool liftCaptured;

    // button highlight restore
    private RectTransform lastRt;
    private Vector3 lastBaseScale;
    private Image lastImg;
    private Color lastBaseColor;
    private Button lastBtn;
    private Selectable.Transition lastTransition;

    private void OnEnable()
    {
        // If the script is attached to center (main), bring main to the front among siblings (so swipe/hold input is received on top)
        // With DirectSelect + number-button tap, it could cover number buttons under the same parent so raycasts don't reach the buttons → skip
        if (center != null && TryGetComponent<RectTransform>(out var rt) && rt == center)
        {
            if (experiment != null && experiment.UsesDirectNumberButtonInput())
                return;
            center.SetAsLastSibling();
        }
    }

    /// <summary> Use when a child of main intercepts touch and dragging fails. Creates a touch-only child under main and
    /// moves the RadialSwipeSelector there so the swipe receives touch. (editor only) </summary>
    [ContextMenu("Create Touch Overlay Under Main (when dragging fails)")]
    private void CreateTouchOverlayUnderMain()
    {
        if (center == null) return;
        var parent = center as RectTransform;
        if (parent == null) return;

        var go = new GameObject("SwipeTouchOverlay");
        go.transform.SetParent(parent, false);
        go.transform.SetAsLastSibling();

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.01f);
        img.raycastTarget = true;

        Debug.Log("[RadialSwipeSelector] SwipeTouchOverlay created. 1) Remove Radial Swipe Selector from main 2) Add Component Radial Swipe Selector to SwipeTouchOverlay 3) Connect SwipeTouchOverlay to ExperimentManager's Thumb Selector, and set Center/Thumb to main.");
    }

    public void SetTargets(List<(int number, RectTransform rt)> newTargets)
    {
        targets = newTargets ?? new List<(int, RectTransform)>();
        ClearHighlight();
        HighlightSpoke(-1, 0f);

        // Show/hide static triangles according to the active button numbers
        UpdateStaticLinesForActiveTargets();

        if (usePreplacedSpokes)
        {
            if (useInspectorSpokeListOnly)
                ApplyInspectorSpokesOnly(targets.Count);
            else
            {
                RefreshPreplacedSpokesForTargets();
                ApplyPreplacedSpokesToTargets();
            }
        }
        else if (!spokeRebuildScheduled)
        {
            spokeRebuildScheduled = true;
            StartCoroutine(RebuildSpokesNextFrame());
        }

        // When usePreplacedSpokes is on, RebuildSpokes doesn't run, so if there are no spokes in the scene, prefab creation would never happen → fallback
        if (TryScheduleRebuildSpokesWhenMissing())
            Debug.Log("[RadialSwipeSelector] No preplaced spokes → creating from Spoke Prefab (RebuildSpokes).");

        if (targets.Count > 0)
            Debug.Log($"[RadialSwipeSelector] SetTargets: received {targets.Count} targets.");
    }

    /// <summary>If there are targets but zero spokes, schedule a coroutine to create them from the Spoke Prefab. Skipped if preplacedOnly.</summary>
    private bool TryScheduleRebuildSpokesWhenMissing()
    {
        if (!usePreplacedSpokes) return false;
        if (spokeRebuildScheduled) return false;
        if (targets == null || targets.Count == 0) return false;
        if (spokes.Count > 0) return false;
        if (spokePrefab == null)
        {
            Debug.LogWarning("[RadialSwipeSelector] To create spokes, assign a Spoke Prefab. (Radial Swipe Selector → Spoke Prefab)");
            return false;
        }
        if (preplacedOnlyNoRuntimeCreate)
        {
            Debug.LogWarning("[RadialSwipeSelector] There are no spokes but Preplaced Only No Runtime Create is on, so auto-creation is skipped. Turn it off or use Bake Preplaced Spokes.");
            return false;
        }
        if (uiRoot == null || center == null)
        {
            Debug.LogWarning("[RadialSwipeSelector] UI Root or Center is empty, so spokes cannot be created.");
            return false;
        }
        spokeRebuildScheduled = true;
        StartCoroutine(RebuildSpokesNextFrame());
        return true;
    }

    private void ApplyInspectorSpokesOnly(int targetCount)
    {
        EnsurePreplacedSpokesFromHierarchyIfInspectorListEmpty();

        spokes.Clear();
        spokeImgs.Clear();
        spokeBaseLen.Clear();

        for (int i = 0; i < preplacedSpokes.Count; i++)
        {
            RectTransform rt = preplacedSpokes[i];
            if (rt == null) continue;
            spokes.Add(rt);
            var img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            spokeImgs.Add(img);
            spokeBaseLen.Add(Mathf.Max(1f, rt.sizeDelta.x));
        }

        int activeCount = Mathf.Clamp(targetCount, 0, spokes.Count);
        for (int i = 0; i < spokes.Count; i++)
        {
            RectTransform line = spokes[i];
            if (line == null) continue;
            bool shouldShow = i < activeCount;
            line.gameObject.SetActive(shouldShow);
            if (!shouldShow) continue;
            if (i < spokeImgs.Count && spokeImgs[i] != null)
                spokeImgs[i].color = spokeNormalColor;

            if (relayoutSpokesOnSetTargets && targets != null && i < targets.Count && targets[i].rt != null)
            {
                LayoutSpokeFromCenterToTarget(line, targets[i].rt);
                spokeBaseLen[i] = Mathf.Max(1f, line.sizeDelta.x);
            }
        }

        if (targetCount > 0 && spokes.Count == 0)
            Debug.LogWarning("[RadialSwipeSelector] There are 0 spokes. Add Spoke_1… to Preplaced Spokes, or leave it empty to auto-search for Spoke_* names under uiRoot.");

        lastAppliedTargetCount = targetCount;
    }

    /// <summary>When the inspector list is empty, search the whole uiRoot for Spoke_1… names and fill preplacedSpokes.</summary>
    private void EnsurePreplacedSpokesFromHierarchyIfInspectorListEmpty()
    {
        bool any = false;
        for (int i = 0; i < preplacedSpokes.Count; i++)
        {
            if (preplacedSpokes[i] != null) { any = true; break; }
        }
        if (any || uiRoot == null) return;

        var tmp = new List<(int n, RectTransform rt)>();
        var all = uiRoot.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (TryParseSpokeNumberFromName(all[i].name, out int num))
                tmp.Add((num, all[i]));
        }
        tmp.Sort((a, b) => a.n.CompareTo(b.n));
        preplacedSpokes.Clear();
        for (int i = 0; i < tmp.Count; i++)
            preplacedSpokes.Add(tmp[i].rt);
    }

    private IEnumerator RebuildSpokesNextFrame()
    {
        yield return null;
        try
        {
            RebuildSpokes();
            if (spokes.Count == 0 && targets != null && targets.Count > 0 && spokePrefab != null)
            {
                yield return null;
                RebuildSpokes();
            }
            if (usePreplacedSpokes)
                ApplyPreplacedSpokesToTargets();
        }
        finally
        {
            spokeRebuildScheduled = false;
        }
    }

    private void RebuildSpokes()
    {
        for (int i = 0; i < spokes.Count; i++)
            if (spokes[i] != null) Destroy(spokes[i].gameObject);
        spokes.Clear();
        spokeImgs.Clear();
        spokeBaseLen.Clear();

        if (uiRoot == null || center == null || targets == null || targets.Count == 0)
        {
            if (targets != null && targets.Count == 0)
                Debug.LogWarning("[RadialSwipeSelector] RebuildSpokes: targets is empty, so no lines are created. Check that the spawner creates number buttons and that RegisterNumberButtons is called.");
            return;
        }
        if (spokePrefab == null)
        {
            Debug.LogWarning("[RadialSwipeSelector] RebuildSpokes: Spoke Prefab is empty, so no lines are created. Assign a prefab to the Radial Swipe Selector's Spoke Prefab.");
            return;
        }

        Transform parent = uiRoot;
        // Convert the fingerprint button's visual exact center (rect-based) to Canvas local
        Vector3 c3 = uiRoot.InverseTransformPoint(GetRectCenterWorld(center));
        Vector2 c = new Vector2(c3.x, c3.y);

        for (int i = 0; i < targets.Count; i++)
        {
            var rt = targets[i].rt;
            if (rt == null) continue;

            Vector3 t3 = uiRoot.InverseTransformPoint(rt.position);
            Vector2 t = new Vector2(t3.x, t3.y);

            Vector2 v = t - c;
            float len = v.magnitude;
            if (len < 1f) len = 1f;

            float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

            RectTransform line = Instantiate(spokePrefab, parent) as RectTransform;
            line.name = $"Spoke_{targets[i].number}";
            line.pivot = new Vector2(0f, 0.5f);
            line.anchorMin = new Vector2(0.5f, 0.5f);
            line.anchorMax = new Vector2(0.5f, 0.5f);
            line.anchoredPosition = c;
            line.sizeDelta = new Vector2(len, spokeThickness);
            line.localEulerAngles = new Vector3(0, 0, ang);
            line.localScale = Vector3.one;
            line.gameObject.SetActive(true);
            line.SetAsLastSibling();

            var img = line.GetComponent<Image>();
            if (img == null) img = line.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = spokeNormalColor;

            spokes.Add(line);
            spokeImgs.Add(img);
            spokeBaseLen.Add(len);
        }
        if (spokes.Count > 0)
            Debug.Log($"[RadialSwipeSelector] RebuildSpokes: created {spokes.Count} lines (Spoke_1~Spoke_{spokes.Count}). Check the Canvas children in the Hierarchy while playing.");
    }

    private Transform GetSpokeParentTransform()
    {
        return preplacedSpokeParent != null ? preplacedSpokeParent : (spokeParent != null ? spokeParent : uiRoot);
    }

    /// <summary>
    /// Combines Spoke_* in the scene + the Inspector list for use.
    /// If preplacedOnlyNoRuntimeCreate=true, nothing is created at runtime.
    /// </summary>
    private void RefreshPreplacedSpokesForTargets()
    {
        Transform parent = GetSpokeParentTransform();
        if ((parent == null && !useInspectorSpokeListOnly) || uiRoot == null || center == null) return;

        var byNumber = new Dictionary<int, RectTransform>();

        void TryAdd(RectTransform rt)
        {
            if (rt == null) return;
            if (!TryParseSpokeNumberFromName(rt.name, out int num)) return;
            if (!byNumber.ContainsKey(num)) byNumber[num] = rt;
        }

        if (useInspectorSpokeListOnly)
        {
            for (int i = 0; i < preplacedSpokes.Count; i++)
                TryAdd(preplacedSpokes[i]);
            if (byNumber.Count == 0 && uiRoot != null)
            {
                var all = uiRoot.GetComponentsInChildren<RectTransform>(true);
                for (int i = 0; i < all.Length; i++)
                    TryAdd(all[i]);
            }
        }
        else
        {
            var allChildren = parent.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < allChildren.Length; i++)
                TryAdd(allChildren[i]);
            for (int i = 0; i < preplacedSpokes.Count; i++)
                TryAdd(preplacedSpokes[i]);
        }

        if (!useInspectorSpokeListOnly && !preplacedOnlyNoRuntimeCreate && targets != null && spokePrefab != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                int num = targets[i].number;
                if (byNumber.ContainsKey(num)) continue;

                RectTransform line = Instantiate(spokePrefab, parent) as RectTransform;
                line.name = $"Spoke_{num}";
                var img = line.GetComponent<Image>();
                if (img == null) img = line.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = spokeNormalColor;
                line.pivot = new Vector2(0f, 0.5f);
                line.anchorMin = new Vector2(0.5f, 0.5f);
                line.anchorMax = new Vector2(0.5f, 0.5f);
                line.localScale = Vector3.one;
                line.gameObject.SetActive(false);

                RectTransform targetRt = targets[i].rt;
                if (targetRt != null)
                    LayoutSpokeFromCenterToTarget(line, targetRt);
                else
                    line.sizeDelta = new Vector2(100f, spokeThickness);

                byNumber[num] = line;
            }
        }
        else if (targets != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (!byNumber.ContainsKey(targets[i].number))
                    Debug.LogWarning($"[RadialSwipeSelector] Spoke_{targets[i].number} is not in the hierarchy. (preplacedOnlyNoRuntimeCreate={preplacedOnlyNoRuntimeCreate})");
            }
        }

        preplacedSpokes.Clear();
        var keys = new List<int>(byNumber.Keys);
        keys.Sort();
        for (int i = 0; i < keys.Count; i++)
            preplacedSpokes.Add(byNumber[keys[i]]);
    }

    private void LayoutSpokeFromCenterToTarget(RectTransform line, RectTransform targetRt)
        => LayoutLineFromCenterToTarget(line, targetRt, spokeThickness);

    /// <summary>
    /// Places a static triangle (line): base at the fingerprint (center) center (default startGap=0), width = fingerprint diameter,
    /// sets length/angle so the apex points toward the target button.
    /// Looks as intended when the prefab is a triangle sprite with 'base = left edge (pivot x=0), apex = right edge'.
    /// </summary>
    private void LayoutStaticTriangleFromCenterToTarget(RectTransform line, RectTransform targetRt)
    {
        if (line == null || targetRt == null || center == null) return;
        Transform lp = line.parent;
        if (lp == null) return;

        Vector3 cW = GetRectCenterWorld(center);
        Vector3 tW = GetRectCenterWorld(targetRt);
        Vector3 vW = tW - cW;
        float fullLen = vW.magnitude;
        if (fullLen < 1e-4f) fullLen = 1f;
        Vector3 dirW = vW / fullLen;

        float startGap = Mathf.Max(0f, staticLineStartGapPx);
        float endGap = Mathf.Max(0f, staticLineEndGapPx);
        float lenWorld = Mathf.Max(1f, fullLen - startGap - endGap);

        Vector3 startW = cW + dirW * startGap;
        Vector3 endW = startW + dirW * lenWorld;

        Vector3 startL = lp.InverseTransformPoint(startW);
        Vector3 endL = lp.InverseTransformPoint(endW);

        Vector2 seg = new Vector2(endL.x - startL.x, endL.y - startL.y);
        float ang = Mathf.Atan2(seg.y, seg.x) * Mathf.Rad2Deg;

        line.pivot = new Vector2(0f, 0.5f);
        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.anchoredPosition = new Vector2(startL.x, startL.y);
        line.localEulerAngles = new Vector3(0f, 0f, ang);

        float baseWidth = GetStaticLineBaseWidth(lp, targetRt);
        line.sizeDelta = new Vector2(Mathf.Max(1f, seg.magnitude), baseWidth);
    }

    /// <summary>Base width of the static line (in the line parent's local units). In diameter mode, converts the 'target button' diameter by the parent scale.</summary>
    private float GetStaticLineBaseWidth(Transform lineParent, RectTransform targetRt)
    {
        if (!staticLineBaseFromCenterDiameter)
            return Mathf.Max(1f, staticLineThickness);

        float diameterWorld = GetRectWorldDiameter(targetRt);
        float parentScale = lineParent != null ? Mathf.Abs(lineParent.lossyScale.x) : 1f;
        if (parentScale < 1e-5f) parentScale = 1f;
        float baseLocal = diameterWorld / parentScale;
        return Mathf.Max(1f, baseLocal * Mathf.Max(0.01f, staticLineBaseWidthMultiplier));
    }

    /// <summary>
    /// World diameter of a RectTransform. If the button Image uses Preserve Aspect (square sprite), the visible circle fits the 'smaller side' of width/height,
    /// so the smaller side (Min) is used as the diameter to keep the base from getting wider than the circle.
    /// </summary>
    private static float GetRectWorldDiameter(RectTransform rt)
    {
        if (rt == null) return 1f;
        rt.GetWorldCorners(s_staticCorners);
        float w = Vector3.Distance(s_staticCorners[0], s_staticCorners[3]); // BL→BR
        float h = Vector3.Distance(s_staticCorners[0], s_staticCorners[1]); // BL→TL
        return Mathf.Max(1f, Mathf.Min(w, h));
    }

    private void LayoutLineFromCenterToTarget(RectTransform line, RectTransform targetRt, float thickness)
    {
        if (line == null || targetRt == null || center == null) return;
        Transform lp = line.parent;
        if (lp == null) return;

        Vector3 cW = GetRectCenterWorld(center);
        Vector3 tW = GetRectCenterWorld(targetRt);
        Vector3 vW = tW - cW;
        float fullLen = vW.magnitude;
        if (fullLen < 1e-4f) fullLen = 1f;
        Vector3 dirW = vW / fullLen;
        float lenWorld = Mathf.Max(1f, fullLen - Mathf.Max(0f, spokeStartGapPx) - Mathf.Max(0f, spokeEndGapPx));

        Vector3 startW = cW + dirW * Mathf.Max(0f, spokeStartGapPx);
        Vector3 endW = startW + dirW * lenWorld;

        Vector3 startL = lp.InverseTransformPoint(startW);
        Vector3 endL = lp.InverseTransformPoint(endW);
        line.anchoredPosition = new Vector2(startL.x, startL.y);

        Vector2 seg = new Vector2(endL.x - startL.x, endL.y - startL.y);
        float ang = Mathf.Atan2(seg.y, seg.x) * Mathf.Rad2Deg;

        line.pivot = new Vector2(0f, 0.5f);
        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.localEulerAngles = new Vector3(0f, 0f, ang);
        line.sizeDelta = new Vector2(Mathf.Max(1f, seg.magnitude), thickness);
    }

    private static bool TryParseSpokeNumberFromName(string objectName, out int number)
    {
        number = 0;
        if (string.IsNullOrEmpty(objectName)) return false;
        if (!objectName.StartsWith("Spoke_", StringComparison.OrdinalIgnoreCase)) return false;

        int i = 6; // after "Spoke_"
        int start = i;
        while (i < objectName.Length && char.IsDigit(objectName[i]))
            i++;
        if (i <= start) return false;

        string digits = objectName.Substring(start, i - start);
        return int.TryParse(digits, out number) && number > 0;
    }

    [ContextMenu("Bake Preplaced Spokes (Max Count)")]
    private void BakePreplacedSpokes()
    {
        if (spokePrefab == null)
        {
            Debug.LogError("[RadialSwipeSelector] spokePrefab is empty, so baking is not possible.");
            return;
        }

        Transform parent = preplacedSpokeParent != null ? preplacedSpokeParent : (spokeParent != null ? spokeParent : uiRoot);
        if (parent == null)
        {
            Debug.LogError("[RadialSwipeSelector] preplacedSpokeParent/spokeParent/uiRoot are all empty, so baking is not possible.");
            return;
        }

        ClearPreplacedSpokes();

        int count = Mathf.Max(0, maxPreplacedSpokeCount);
        for (int i = 0; i < count; i++)
        {
            RectTransform line = Instantiate(spokePrefab, parent);
            line.name = $"Spoke_{i + 1}";
            line.gameObject.SetActive(true);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchorMin = new Vector2(0.5f, 0.5f);
            line.anchorMax = new Vector2(0.5f, 0.5f);
            line.anchoredPosition = Vector2.zero;
            line.localEulerAngles = Vector3.zero;
            line.localScale = Vector3.one;
            line.sizeDelta = new Vector2(100f, spokeThickness);

            var img = line.GetComponent<Image>();
            if (img == null) img = line.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = spokeNormalColor;

            preplacedSpokes.Add(line);
        }

        Debug.Log($"[RadialSwipeSelector] Finished creating {count} preplaced spokes (Spoke_1~Spoke_{count})");
        ApplyPreviewLayoutAfterBake();
    }

    [ContextMenu("Clear Preplaced Spokes")]
    private void ClearPreplacedSpokes()
    {
        for (int i = 0; i < preplacedSpokes.Count; i++)
        {
            RectTransform rt = preplacedSpokes[i];
            if (rt == null) continue;
            if (Application.isPlaying) Destroy(rt.gameObject);
            else DestroyImmediate(rt.gameObject);
        }
        preplacedSpokes.Clear();
    }

    /// <summary>
    /// Bakes 'static lines' extending from the fingerprint button (center) center to each button in previewTargets.
    /// Always visible independent of the swipe highlight system (Spoke_* names/highlighting), with no effects like color/length changes.
    /// </summary>
    [ContextMenu("Bake Static Lines (fingerprint→button, no effects)")]
    private void BakeStaticLinesToPreviewTargets()
    {
        if (center == null)
        {
            Debug.LogError("[RadialSwipeSelector] Center (fingerprint button) is empty, so static lines cannot be baked.");
            return;
        }
        if (previewTargets == null || previewTargets.Count == 0)
        {
            Debug.LogWarning("[RadialSwipeSelector] previewTargets is empty, so static lines cannot be created. Add buttons to the inspector's Preview Targets in order starting from #1.");
            return;
        }

        Transform parent = staticLineParent != null ? staticLineParent : GetSpokeParentTransform();
        if (parent == null)
        {
            Debug.LogError("[RadialSwipeSelector] Static Line Parent/Spoke Parent/uiRoot are all empty, so static lines cannot be baked.");
            return;
        }

        ClearStaticLines();
        // Force the world coordinates of the just-created RectTransform to update in the editor
        Canvas.ForceUpdateCanvases();

        int made = 0;
        for (int i = 0; i < previewTargets.Count; i++)
        {
            RectTransform target = previewTargets[i];
            if (target == null) continue;

            // Prefer the button's logical number (NumberButton.Number); otherwise the order (i+1)
            int num = GetTargetLogicalNumber(target, i + 1);

            // Place under the same parent so it sits below (behind) the button (optional)
            Transform lineParent = (staticLinesUnderButtons && target.parent != null) ? target.parent : parent;

            var go = new GameObject($"StaticLine_{num}", typeof(RectTransform), typeof(UITriangleGraphic));
            RectTransform line = go.GetComponent<RectTransform>();
            line.SetParent(lineParent, false);
            line.localScale = Vector3.one;

            var tri = go.GetComponent<UITriangleGraphic>();
            tri.raycastTarget = false;
            tri.color = staticLineColor;

            // Its own Canvas (overrideSorting) to send it behind buttons regardless of sibling order
            if (staticLinesUseSortingCanvas)
            {
                var cv = go.GetComponent<Canvas>();
                if (cv == null) cv = go.AddComponent<Canvas>();
                cv.overrideSorting = true;
                cv.sortingOrder = staticLineSortingOrder;
            }

            LayoutStaticTriangleFromCenterToTarget(line, target);
            if (staticLinesBehindSiblings) line.SetAsFirstSibling();

            staticLines.Add(line);
            made++;
        }

        Debug.Log($"[RadialSwipeSelector] Finished baking {made} static lines. They extend with the apex at the fingerprint center and the base (diameter={(staticLineBaseFromCenterDiameter ? "button diameter" : staticLineThickness + "px")}) toward the button. Auto-shown/hidden according to the number of active buttons.");
    }

    [ContextMenu("Clear Static Lines")]
    private void ClearStaticLines()
    {
        for (int i = 0; i < staticLines.Count; i++)
        {
            RectTransform rt = staticLines[i];
            if (rt == null) continue;
            if (Application.isPlaying) Destroy(rt.gameObject);
            else DestroyImmediate(rt.gameObject);
        }
        staticLines.Clear();
    }

    /// <summary>Shows only the static triangles matching the current active target (targets) numbers and hides the rest.</summary>
    private void UpdateStaticLinesForActiveTargets()
    {
        if (staticLines == null || staticLines.Count == 0) return;

        // Set of active numbers. If there are no targets, hide all.
        var activeNumbers = new HashSet<int>();
        if (targets != null)
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].rt != null)
                    activeNumbers.Add(targets[i].number);
        }

        for (int i = 0; i < staticLines.Count; i++)
        {
            RectTransform line = staticLines[i];
            if (line == null) continue;
            bool parsed = TryParseStaticLineNumber(line.name, out int num);
            bool show = parsed && activeNumbers.Contains(num);
            if (line.gameObject.activeSelf != show)
                line.gameObject.SetActive(show);
            if (!show) continue;

            // Move the triangle to the first child of the root canvas to always send it behind the number buttons (bottom). (position must be recomputed since the parent changes)
            MoveStaticLineBehindButtons(line);

            // If the spawner repositions buttons at runtime (relayoutOnSetCount), baked lines stay in their old spots, so redraw them at the current button positions
            if (relayoutStaticLinesOnSetTargets && TryGetActiveTargetByNumber(num, out RectTransform targetRt))
                LayoutStaticTriangleFromCenterToTarget(line, targetRt);
        }
    }

    /// <summary>
    /// Forces static triangles to always sit below the number buttons.
    /// Since the button parent (ButtonsParent) is a plain Transform, uGUI sibling ordering under it is unstable,
    /// so the triangle is moved to the first child of the root canvas (a real RectTransform). → Drawn before the ButtonsParent that holds the buttons (= behind).
    /// If an overrideSorting Canvas is attached, it ignores sibling order, so it is removed.
    /// </summary>
    private void MoveStaticLineBehindButtons(RectTransform line)
    {
        if (line == null) return;

        var cv = line.GetComponent<Canvas>();
        if (cv != null) Destroy(cv);

        Transform root = uiRoot != null ? (Transform)uiRoot : line.parent;
        if (root != null && line.parent != root)
            line.SetParent(root, false);

        line.SetAsFirstSibling();
    }

    private bool TryGetActiveTargetByNumber(int number, out RectTransform rt)
    {
        rt = null;
        if (targets == null) return false;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].number == number && targets[i].rt != null)
            {
                rt = targets[i].rt;
                return true;
            }
        }
        return false;
    }

    private int GetTargetLogicalNumber(RectTransform target, int fallback)
    {
        if (target == null) return fallback;
        var nb = target.GetComponent<NumberButton>() ?? target.GetComponentInChildren<NumberButton>(true);
        return nb != null && nb.Number > 0 ? nb.Number : fallback;
    }

    private static bool TryParseStaticLineNumber(string objectName, out int number)
    {
        number = 0;
        if (string.IsNullOrEmpty(objectName)) return false;
        const string prefix = "StaticLine_";
        if (!objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        int i = prefix.Length;
        int start = i;
        while (i < objectName.Length && char.IsDigit(objectName[i]))
            i++;
        if (i <= start) return false;

        return int.TryParse(objectName.Substring(start, i - start), out number) && number > 0;
    }

    [ContextMenu("Preview Preplaced Spokes From Preview Targets")]
    private void ApplyPreviewLayoutAfterBake()
    {
        if (previewTargets == null || previewTargets.Count == 0)
        {
            Debug.LogWarning("[RadialSwipeSelector] previewTargets is empty, so the post-Bake preview alignment is skipped.");
            return;
        }

        var preview = new List<(int number, RectTransform rt)>();
        for (int i = 0; i < previewTargets.Count; i++)
        {
            if (previewTargets[i] == null) continue;
            preview.Add((i + 1, previewTargets[i]));
        }
        if (preview.Count == 0)
        {
            Debug.LogWarning("[RadialSwipeSelector] previewTargets has no valid RectTransform.");
            return;
        }

        targets = preview;
        RefreshPreplacedSpokesForTargets();
        // Editor Bake must perform alignment and activation even when usePreplacedSpokes is off
        ApplyPreplacedSpokesToTargets(forEditorBake: true);
        HighlightSpoke(-1, 0f);
    }

    private void ApplyPreplacedSpokesToTargets(bool forEditorBake = false)
    {
        if ((!usePreplacedSpokes && !forEditorBake) || uiRoot == null || center == null) return;
        if (targets == null) return;
        
        // Sync every time so Spoke_1..N always exist/are mapped even as N grows
        RefreshPreplacedSpokesForTargets();

        // Rebuild the internal cache to always reference the current preplacedSpokes
        spokes.Clear();
        spokeImgs.Clear();
        spokeBaseLen.Clear();
        for (int i = 0; i < preplacedSpokes.Count; i++)
        {
            RectTransform rt = preplacedSpokes[i];
            if (rt == null) continue;
            spokes.Add(rt);
            var img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            spokeImgs.Add(img);
            spokeBaseLen.Add(1f);
        }

        // Hide all preplaced spokes
        for (int i = 0; i < spokes.Count; i++)
            if (spokes[i] != null)
                spokes[i].gameObject.SetActive(false);
        // A Spoke_* not in the cache may remain under the parent, so hide the parent's spokes once too
        HideAllSpokesUnderParent();

        // Inspector-list-only mode: force-activate by index without names/searching
        if (useInspectorSpokeListOnly)
        {
            int activeCount = Mathf.Min(targets.Count, spokes.Count);
            for (int i = 0; i < spokes.Count; i++)
            {
                RectTransform line = spokes[i];
                if (line == null) continue;
                bool shouldShow = i < activeCount;
                line.gameObject.SetActive(shouldShow);
                if (!shouldShow) continue;

                if (i < spokeImgs.Count && spokeImgs[i] != null)
                    spokeImgs[i].color = spokeNormalColor;
                spokeBaseLen[i] = Mathf.Max(1f, line.sizeDelta.x);

                if (relayoutSpokesOnSetTargets && i < targets.Count && targets[i].rt != null)
                {
                    LayoutSpokeFromCenterToTarget(line, targets[i].rt);
                    line.localScale = Vector3.one;
                    spokeBaseLen[i] = Mathf.Max(1f, line.sizeDelta.x);
                }
            }
            lastAppliedTargetCount = targets.Count;
            return;
        }

        if (relayoutSpokesOnSetTargets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                int num = targets[i].number;
                RectTransform targetRt = targets[i].rt;
                if (targetRt == null) continue;
                int idx = FindSpokeIndexByNumber(num);
                if (idx < 0 || idx >= spokes.Count) continue;
                RectTransform line = spokes[idx];
                LayoutSpokeFromCenterToTarget(line, targetRt);
                line.localScale = Vector3.one;
                spokeBaseLen[idx] = Mathf.Max(1f, line.sizeDelta.x);
            }
        }

        lastAppliedTargetCount = targets.Count;
    }

    private void HideAllSpokesUnderParent()
    {
        if (useInspectorSpokeListOnly)
        {
            for (int i = 0; i < preplacedSpokes.Count; i++)
            {
                RectTransform rt = preplacedSpokes[i];
                if (rt != null) rt.gameObject.SetActive(false);
            }
            return;
        }

        Transform parent = GetSpokeParentTransform();
        if (parent == null) return;
        var allChildren = parent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            RectTransform rt = allChildren[i];
            if (rt != null &&
                TryParseSpokeNumberFromName(rt.name, out _))
            {
                rt.gameObject.SetActive(false);
            }
        }
    }

    private RectTransform FindSpokeInParentByNumber(int number)
    {
        if (useInspectorSpokeListOnly)
        {
            string exactName = $"Spoke_{number}";
            for (int i = 0; i < preplacedSpokes.Count; i++)
            {
                RectTransform rt = preplacedSpokes[i];
                if (rt == null) continue;
                if (string.Equals(rt.name, exactName, StringComparison.OrdinalIgnoreCase)) return rt;
            }
            for (int i = 0; i < preplacedSpokes.Count; i++)
            {
                RectTransform rt = preplacedSpokes[i];
                if (rt == null) continue;
                if (TryParseSpokeNumberFromName(rt.name, out int n) && n == number) return rt;
            }
            return null;
        }

        Transform parent = GetSpokeParentTransform();
        if (parent == null) return null;
        var allChildren = parent.GetComponentsInChildren<RectTransform>(true);

        // 1) Prefer the exact name
        string exact = $"Spoke_{number}";
        for (int i = 0; i < allChildren.Length; i++)
        {
            RectTransform rt = allChildren[i];
            if (rt != null && string.Equals(rt.name, exact, StringComparison.OrdinalIgnoreCase))
            {
                return rt;
            }
        }
        // 2) Allow names with suffixes (e.g. Spoke_5(Clone))
        for (int i = 0; i < allChildren.Length; i++)
        {
            RectTransform rt = allChildren[i];
            if (rt != null && TryParseSpokeNumberFromName(rt.name, out int n) && n == number)
                return rt;
        }
        return null;
    }

    private int EnsureSpokeCached(RectTransform rt)
    {
        if (rt == null) return -1;
        for (int i = 0; i < spokes.Count; i++)
            if (spokes[i] == rt) return i;

        spokes.Add(rt);
        var img = rt.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        spokeImgs.Add(img);
        spokeBaseLen.Add(Mathf.Max(1f, rt.sizeDelta.x));
        return spokes.Count - 1;
    }

    private int FindSpokeIndexByNumber(int number)
    {
        if (useInspectorSpokeListOnly)
        {
            int idx = number - 1;
            if (idx >= 0 && idx < spokes.Count && spokes[idx] != null)
                return idx;
            return -1;
        }

        for (int i = 0; i < spokes.Count; i++)
        {
            RectTransform rt = spokes[i];
            if (rt == null) continue;
            if (TryParseSpokeNumberFromName(rt.name, out int num) && num == number)
                return i;
        }
        return -1;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (uiRoot == null || center == null || experiment == null) return;

        isDown = true;
        candidate = -1;
        candidateRt = null;
        liftRealtime = -1.0;
        liftCaptured = false;
        pointerDownTime = Time.unscaledTime;
        pointerDownPointCaptured = TryGetLocalPointerPoint(eventData, out pointerDownLocalPoint);

        bool thumbIsCenter = thumb != null && thumb == center;
        if (thumbIsCenter && moveThumbWithSwipe)
        {
            Debug.LogWarning("[RadialSwipeSelector] Thumb is the same as Center but Move Thumb With Swipe is on, so it is turned off automatically.");
            moveThumbWithSwipe = false;
        }

        if (thumb != null && moveThumbWithSwipe)
        {
            thumb.gameObject.SetActive(true);
            var img = thumb.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }
        else if (thumb != null && thumb != center)
            thumb.gameObject.SetActive(false);

        experiment.MainDown();
        TryScheduleRebuildSpokesWhenMissing();
        UpdateCandidate(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDown) return;
        UpdateCandidate(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isDown) return;
        isDown = false;

        // Touchdown: record the finger-release moment that confirms the selection, using the same monotonic clock as lift.
        double touchdownRealtime = Time.realtimeSinceStartupAsDouble;

        experiment.MainUp();

        if (thumb != null)
        {
            if (moveThumbWithSwipe && thumb != center)
                thumb.anchoredPosition = center.anchoredPosition;
            if (thumb != center)
                thumb.gameObject.SetActive(false);
        }

        int chosen = candidate;

        ClearHighlight();
        HighlightSpoke(-1, 0f);

        candidate = -1;
        candidateRt = null;

        if (confirmOnPointerUp && chosen != -1)
            experiment.OnNumberPressed(chosen, eventData.position, "swipe",
                liftCaptured ? liftRealtime : -1.0, touchdownRealtime);
    }

    /// <summary> World coordinates of the Rect's geometric center (same as fingerprint/main and button alignment). </summary>
    private static Vector3 GetRectCenterWorld(RectTransform rt)
    {
        if (rt == null) return Vector3.zero;
        return rt.TransformPoint(rt.rect.center);
    }

    private Camera GetCanvasCamera()
    {
        if (uiRoot == null) return Camera.main;
        var canvas = uiRoot.GetComponent<Canvas>();
        if (canvas == null) return Camera.main;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;  // Overlay has no camera
        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private void UpdateCandidate(PointerEventData eventData)
    {
        if (targets == null || targets.Count == 0) return;

        if (!TryGetLocalPointerPoint(eventData, out Vector2 localPoint))
            return;
        Vector2 c = GetCenterLocalPoint();

        // Noise filter 1) Ignore a brief period right after pointer down
        float elapsedMs = (Time.unscaledTime - pointerDownTime) * 1000f;
        if (elapsedMs < Mathf.Max(0f, noiseIgnoreTimeMs))
        {
            SetCandidate(-1, null);
            HighlightSpoke(-1, 0f);
            return;
        }

        // Noise filter 2) Ignore if movement from the down point is below the minimum
        if (pointerDownPointCaptured)
        {
            float moved = Vector2.Distance(pointerDownLocalPoint, localPoint);
            if (moved < Mathf.Max(0f, noiseMinMovePx))
            {
                SetCandidate(-1, null);
                HighlightSpoke(-1, 0f);
                return;
            }
        }

        Vector2 v = (Vector2)localPoint - c;
        float len = v.magnitude;

        if (len < minDragFromCenterPx)
        {
            SetCandidate(-1, null);
            HighlightSpoke(-1, 0f);
            return;
        }

        // Movement onset (lift-off): record the first moment the finger 'definitively' leaves the origin, only after the stimulus is presented (while waiting for a response).
        if (!liftCaptured && experiment != null && experiment.IsWaitingForResponse)
        {
            liftRealtime = Time.realtimeSinceStartupAsDouble;
            liftCaptured = true;
        }

        Vector2 dir = v / len;

        // ✅ Angle-based: find the closest button
        float fingerAngleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        float bestAngleDiff = 999f;
        int bestNum = -1;
        RectTransform bestRt = null;
        Vector2 bestTargetDir = dir;

        for (int i = 0; i < targets.Count; i++)
        {
            var rt = targets[i].rt;
            if (rt == null) continue;

            Vector3 t3 = uiRoot.InverseTransformPoint(GetRectCenterWorld(rt));
            Vector2 t = new Vector2(t3.x, t3.y);

            Vector2 toTarget = (t - c).normalized;
            float targetAngleDeg = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            float diff = Mathf.Abs(Mathf.DeltaAngle(fingerAngleDeg, targetAngleDeg));

            if (diff < bestAngleDiff)
            {
                bestAngleDiff = diff;
                bestNum = targets[i].number;
                bestRt = rt;
                bestTargetDir = toTarget; // ✅ snap toward the candidate button direction
            }
        }

        SetCandidate(bestNum, bestRt);
        float targetLen = GetSpokeLength(bestNum);
        float projected = Vector2.Dot(v, bestTargetDir);
        float progress = targetLen <= 1f ? 1f : Mathf.Clamp01(projected / targetLen);
        HighlightSpoke(bestNum, progress);

        if (moveThumbWithSwipe && thumb != null)
        {
            Vector2 thumbPosInUiRoot = c + bestTargetDir * thumbRadiusPx;
            thumb.position = uiRoot.TransformPoint(thumbPosInUiRoot);
        }
    }

    private float GetSpokeLength(int number)
    {
        int idx = FindSpokeIndexByNumber(number);
        if (idx < 0 || idx >= spokeBaseLen.Count) return 0f;
        return spokeBaseLen[idx];
    }

    private void HighlightSpoke(int activeNumber, float progress)
    {
        float clampedProgress = Mathf.Clamp01(progress);

        for (int i = 0; i < spokes.Count; i++)
        {
            if (spokes[i] == null) continue;

            bool active = TryParseSpokeNumberFromName(spokes[i].name, out int spokeNum) && spokeNum == activeNumber;
            float thick = active ? spokeActiveThickness : spokeThickness;
            float baseLen = i < spokeBaseLen.Count ? spokeBaseLen[i] : spokes[i].sizeDelta.x;
            float len = baseLen;
            if (active)
            {
                // The feel of "color/length spreading" from the center toward the button
                float minLen = Mathf.Max(1f, baseLen * activeSpokeMinLengthRatio);
                len = Mathf.Lerp(minLen, baseLen, clampedProgress);
            }

            var sd = spokes[i].sizeDelta;
            spokes[i].sizeDelta = new Vector2(len, thick);

            var img = spokeImgs[i];
            if (img != null)
                img.color = active ? spokeActiveColor : spokeNormalColor;
        }
    }

    private void SetCandidate(int num, RectTransform rt)
    {
        if (candidate == num && candidateRt == rt) return;

        candidate = num;
        candidateRt = rt;

        ClearHighlight();

        if (rt == null) return;

        lastRt = rt;
        lastBaseScale = rt.localScale;

        lastBtn = rt.GetComponent<Button>() ?? rt.GetComponentInChildren<Button>(true);
        if (forceButtonTransitionNone && lastBtn != null)
        {
            lastTransition = lastBtn.transition;
            lastBtn.transition = Selectable.Transition.None;
        }

        lastImg = rt.GetComponent<Image>() ?? rt.GetComponentInChildren<Image>(true);
        if (lastImg != null)
        {
            lastBaseColor = lastImg.color;
            lastImg.color = highlightColor;
        }

        rt.localScale = lastBaseScale * highlightScale;
    }

    private void ClearHighlight()
    {
        if (lastRt != null)
        {
            lastRt.localScale = lastBaseScale;

            if (lastImg != null)
                lastImg.color = lastBaseColor;

            if (forceButtonTransitionNone && lastBtn != null)
                lastBtn.transition = lastTransition;
        }

        lastRt = null;
        lastImg = null;
        lastBtn = null;
    }

    private bool TryGetLocalPointerPoint(PointerEventData eventData, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (uiRoot == null) return false;

        var cam = eventData != null ? (eventData.pressEventCamera ?? GetCanvasCamera()) : GetCanvasCamera();
        if (cam != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                uiRoot, eventData.position, cam, out localPoint);
            return true;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            uiRoot, eventData.position, null, out localPoint);
        return true;
    }

    private Vector2 GetCenterLocalPoint()
    {
        Vector3 c3 = uiRoot.InverseTransformPoint(GetRectCenterWorld(center));
        return new Vector2(c3.x, c3.y);
    }
}