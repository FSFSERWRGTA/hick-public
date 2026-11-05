using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GridNumberButtonLayout : NumberButtonSpawnerBase
{
    public enum GridPattern
    {
        [UnityEngine.InspectorName("Direct (use Columns)")]
        Custom,
        [UnityEngine.InspectorName("2 rows × 4 columns (8 total, 4+4)")]
        TwoRowsByFourColumns,
        [UnityEngine.InspectorName("4-column grid (auto rows, up to 8 slots)")]
        FourColumnsUpToEight,
    }

    public enum ButtonLayoutMode
    {
        // ⚠ The first value (0) must be Manual: existing scenes without this field load as 0, preserving the existing behavior
        [UnityEngine.InspectorName("Manual (control the individual flags below directly)")]
        Manual,
        [UnityEngine.InspectorName("Column-aligned grid (revised · variable Fitts size)")]
        ColumnAlignedGrid,
        [UnityEngine.InspectorName("Per-row auto fill (legacy · variable Fitts size)")]
        MeasuredPerRow,
        [UnityEngine.InspectorName("Fixed-size grid (Fitts not applied)")]
        UniformFixedSize,
    }

    [Header("Layout Mode Preset")]
    [Tooltip("Switches between the three layout modes at once. If Manual, the individual flags below (autoApply/fitts/measured) are used directly. If you choose a value other than Manual, those flags are overwritten according to the preset.")]
    [SerializeField] private ButtonLayoutMode layoutMode = ButtonLayoutMode.Manual;

    [Header("References")]
    [Tooltip("Number buttons pre-placed in the scene (in order starting from 1)")]
    [SerializeField] private List<RectTransform> preplacedButtons = new();
    [Tooltip("Original button prefab/template to clone when baking")]
    [SerializeField] private RectTransform buttonPrefab;
    [Tooltip("Parent that the baked buttons go into")]
    [SerializeField] private Transform spawnParent;

    [Header("Bake")]
    [SerializeField] private int maxPreplacedCount = 10;

    [Header("Layout")]
    [SerializeField] private GridPattern gridPattern = GridPattern.Custom;
    [SerializeField] private int columns = 10;
    [SerializeField] private float spacingX = 15f;
    [SerializeField] private float spacingY = 15f;
    [SerializeField] private Vector2 startOffset = new Vector2(0f, 0f);
    [Tooltip("If on, centers the whole grid block relative to the parent (recommended for 4+4, etc.)")]
    [SerializeField] private bool centerGridInParent = false;
    [Tooltip("If on, moves the buttons' centroid to the exact center of the root canvas (screen) after layout")]
    [SerializeField] private bool alignGridToCanvasCenter = false;
    [Tooltip("If on, aligns the horizontal center of the axis-aligned bounding box of all active buttons (including rotation) to the center of the root canvas. In 8-slot (4+4) mode, it always aligns based on all slots 1–8 regardless of N, so the grid does not shift sideways whenever N changes.")]
    [SerializeField] private bool alignGridHorizontalBoundsToRootCanvas = true;
    [Tooltip("When the last row has fewer than 4 columns, centers only that row horizontally within the same full width as a ‘full row’.")]
    [SerializeField] private bool centerEachRowHorizontally = false;
    [Tooltip("When cell sizes vary (e.g., due to Fitts), uses the actual Rect (including rotation) size to determine row width/height and aligns the vertical center of the same row.")]
    [SerializeField] private bool rowLayoutFromMeasuredExtents = true;
    [Tooltip("If on, recalculates grid coordinates at runtime (instead of manual editor placement). Leave off for the default Hick scene.")]
    [SerializeField] private bool autoApplyGridLayoutInPlay = false;
    [Tooltip("Turn on only for Fitts/4-column 8-slot dedicated scenes. If on, HickExperiment's max N is limited to 8.")]
    [SerializeField] private bool restrictLayoutToEightTargets = false;

    [Header("Fitts (optional, separate from the default Hick)")]
    [Tooltip("If on, adjusts the target button size according to the distance/angle to the main button. Matching the CanvasScaler reference resolution to the value below makes the px meaning accurate.")]
    [SerializeField] private bool fittsLawSizeFromMain = false;
    [SerializeField] private RectTransform mainButtonReference;
    [Tooltip("Reference canvas resolution (portrait) used for distance/scale conversion. e.g., 1080×2340")]
    [SerializeField] private Vector2 fittsReferenceResolution = new Vector2(1080f, 2340f);
    [Tooltip("Reference radius (px). Used only in LegacyLinear mode: scales so that at this distance a button's side becomes the prefab reference size.")]
    [SerializeField] private float fittsReferenceRadiusPx = 450f;
    [Tooltip("Iso-difficulty (iso-ID) target ID (bits). If 0, automatically back-calculates from fittsReferenceRadiusPx and the reference button size.")]
    [SerializeField] private float fittsTargetIdBits = 0f;
    [Tooltip("FFitts touch ambiguity c (mm). ≈0.285 based on the Yamanaka & Usuba 2021 2D experiment")]
    [SerializeField] private float fittsAmbiguityC_mm = FittsLawModel.DefaultAmbiguityC2D_mm;
    [Tooltip("Screen px/mm. Galaxy S24 Ultra QHD+(3120×1440, 6.8\") ≈19.89")]
    [SerializeField] private float pixelsPerMm = FittsLawModel.DefaultPixelsPerMm;
    [SerializeField] private FittsSizingModel fittsSizingModel = FittsSizingModel.FFittsSqrtIsoId;
    [SerializeField] private float fittsMinScale = 0.35f;
    [SerializeField] private float fittsMaxScale = 2.2f;
    [Tooltip("If on, makes the button rect square (based on the shorter side). With preserveAspect, the visible graphic and the tap area match.")]
    [SerializeField] private bool fittsSquareButtons = true;
    [Tooltip("Minimum physical size (mm) of the smallest button. Guaranteed by converting from device resolution/screen diagonal. If 0, not applied.")]
    [SerializeField] private float fittsMinButtonSizeMm = 9.6f;
    [Tooltip("Device physical screen diagonal (mm). Galaxy S24 Ultra 6.8 inch ≈ 172.72")]
    [SerializeField] private float physicalDiagonalMm = 172.72f;
    [Tooltip("If 0, uses distance only. If >0, adds scaling according to the azimuth from the main button (for thumb arc/corner correction).")]
    [SerializeField] private float fittsAngularWeight = 0f;
    [Tooltip("If on, rotates each number button around Z so it faces the main button.")]
    [SerializeField] private bool orientButtonsTowardMain = false;
    [Tooltip("For auto grid + Fitts, uses the largest button in the current layout (AABB in the root canvas) as the cell size to widen spacing. Repeats several times to reduce overlap.")]
    [SerializeField] private int fittsGridAntiOverlapPasses = 3;
    [Tooltip("Minimum spacing to keep buttons from touching each other during Fitts auto layout (px, in the root canvas).")]
    [SerializeField] private float fittsMinGap = 2f;
    [Tooltip("Extra vertical spacing (px) placed only between rows when there are multiple rows (4+4, etc.). e.g., increase this when you want to widen the gap between rows 1–4 and rows 5–8. No effect on horizontal spacing. If 0, only fittsMinGap is used.")]
    [SerializeField] private float fittsExtraRowGapY = 0f;
    [Tooltip("Edge padding to keep buttons from going off screen during Fitts auto layout (px, in the root canvas).")]
    [SerializeField] private float fittsCanvasPadding = 12f;
    [Tooltip("If on, also adjusts the inner text (Text/TMP) size at the same ratio as the button size scale.")]
    [SerializeField] private bool scaleLabelWithButton = true;

    [Header("Link")]
    [SerializeField] private HickExperiment experiment;

    private readonly List<RectTransform> activeButtons = new();
    private static readonly Vector3[] CornerScratch = new Vector3[4];
    private int currentN;
    private readonly Dictionary<RectTransform, Vector2> baselineSizeDelta = new();
    private readonly Dictionary<RectTransform, Quaternion> baselineLocalRotation = new();
    private readonly Dictionary<Text, int> baselineLegacyFontSize = new();
    private readonly Dictionary<TMP_Text, float> baselineTmpFontSize = new();
    private readonly Dictionary<int, FittsMetrics> fittsMetricsByLogicalNumber = new();

    /// <summary>Converts the layoutMode preset into individual flags. If Manual, changes nothing.</summary>
    void ApplyLayoutModePreset()
    {
        switch (layoutMode)
        {
            case ButtonLayoutMode.ColumnAlignedGrid:
                autoApplyGridLayoutInPlay = true;
                fittsLawSizeFromMain = true;
                rowLayoutFromMeasuredExtents = false;
                break;
            case ButtonLayoutMode.MeasuredPerRow:
                autoApplyGridLayoutInPlay = true;
                fittsLawSizeFromMain = true;
                rowLayoutFromMeasuredExtents = true;
                break;
            case ButtonLayoutMode.UniformFixedSize:
                autoApplyGridLayoutInPlay = true;
                fittsLawSizeFromMain = false;
                rowLayoutFromMeasuredExtents = false;
                break;
            case ButtonLayoutMode.Manual:
            default:
                break;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyLayoutModePreset();

        if (gridPattern == GridPattern.TwoRowsByFourColumns || gridPattern == GridPattern.FourColumnsUpToEight)
            columns = 4;
        if (restrictLayoutToEightTargets)
        {
            columns = 4;
            if (gridPattern == GridPattern.Custom)
                gridPattern = GridPattern.FourColumnsUpToEight;
        }

        fittsGridAntiOverlapPasses = Mathf.Clamp(fittsGridAntiOverlapPasses, 1, 8);
        fittsAmbiguityC_mm = Mathf.Max(0f, fittsAmbiguityC_mm);
        pixelsPerMm = Mathf.Max(1e-3f, pixelsPerMm);
        fittsMinButtonSizeMm = Mathf.Max(0f, fittsMinButtonSizeMm);
        physicalDiagonalMm = Mathf.Max(0f, physicalDiagonalMm);
    }
#endif

    public override bool TryGetFittsMetrics(int logicalNumber, out FittsMetrics metrics)
    {
        return fittsMetricsByLogicalNumber.TryGetValue(logicalNumber, out metrics);
    }

    void Awake()
    {
        ApplyLayoutModePreset();
        CacheBaselinesForPreplaced();
    }

    void CacheBaselinesForPreplaced()
    {
        baselineSizeDelta.Clear();
        baselineLocalRotation.Clear();
        baselineLegacyFontSize.Clear();
        baselineTmpFontSize.Clear();
        if (preplacedButtons == null) return;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            var rt = preplacedButtons[i];
            if (rt == null) continue;
            baselineSizeDelta[rt] = rt.sizeDelta;
            baselineLocalRotation[rt] = rt.localRotation;
            CacheLabelBaselines(rt);
        }
    }

    public override void SetCount(int n)
    {
        int capped = Mathf.Max(0, n);
        if (restrictLayoutToEightTargets)
            capped = Mathf.Clamp(capped, 0, 8);
        currentN = capped;
        ActivateForCount();
    }

    public override int GetMaxSupportedCount()
    {
        if (restrictLayoutToEightTargets)
            return Mathf.Min(8, ComputePreplacedMaxLogicalOrCount());

        return ComputePreplacedMaxLogicalOrCount();
    }

    int ComputePreplacedMaxLogicalOrCount()
    {
        if (preplacedButtons == null || preplacedButtons.Count == 0) return 0;
        int maxSlot = 0;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            var button = preplacedButtons[i];
            if (button == null) continue;
            var nb = button.GetComponent<NumberButton>();
            if (nb != null) maxSlot = Mathf.Max(maxSlot, nb.Number);
        }
        return maxSlot > 0 ? maxSlot : preplacedButtons.Count;
    }

    private void ActivateForCount()
    {
        if (preplacedButtons.Count == 0)
        {
            Debug.LogError("[GridNumberButtonLayout] preplacedButtons is empty. In the Inspector, connect the buttons directly in order starting from 1.");
            return;
        }
        if (preplacedButtons.Count < currentN)
            Debug.LogError($"[GridNumberButtonLayout] Not enough buttons: available={preplacedButtons.Count}, requestedN={currentN}");

        activeButtons.Clear();
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;

            var nb = button.GetComponent<NumberButton>();
            bool shouldShow = nb != null
                ? nb.Number >= 1 && nb.Number <= currentN
                : i < currentN;
            if (button.gameObject.activeSelf != shouldShow)
                button.gameObject.SetActive(shouldShow);
            if (shouldShow)
                activeButtons.Add(button);
        }

        activeButtons.Sort((a, b) =>
        {
            int na = a.GetComponent<NumberButton>()?.Number ?? 0;
            int nbNum = b.GetComponent<NumberButton>()?.Number ?? 0;
            int c = na.CompareTo(nbNum);
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });

        List<RectTransform> gridLayoutTargets = BuildGridLayoutTargetsForCurrentMode();

        if (Application.isPlaying)
        {
            RectTransform sizeSrc = buttonPrefab != null ? buttonPrefab : (gridLayoutTargets.Count > 0 ? gridLayoutTargets[0] : null);
            if (sizeSrc != null)
            {
                bool fittsReady = fittsLawSizeFromMain && mainButtonReference != null;
                Canvas mainCanvas = fittsReady ? mainButtonReference.GetComponentInParent<Canvas>() : null;
                RectTransform rootRtForMeasure = mainCanvas != null ? mainCanvas.rootCanvas.transform as RectTransform : null;

                if (autoApplyGridLayoutInPlay && fittsReady && rootRtForMeasure != null)
                {
                    RestoreBaselinesFor(gridLayoutTargets);
                    ApplyGridLayoutPositions(gridLayoutTargets, sizeSrc, null, null, null, null, false);
                    Canvas.ForceUpdateCanvases();

                    int passes = Mathf.Clamp(fittsGridAntiOverlapPasses, 1, 8);
                    for (int pass = 0; pass < passes; pass++)
                    {
                        ApplyFittsLawSizing(gridLayoutTargets, sizeSrc);
                        Canvas.ForceUpdateCanvases();
                        float maxW = 0f;
                        float maxH = 0f;
                        for (int i = 0; i < gridLayoutTargets.Count; i++)
                        {
                            GetAxisAlignedSizeInRootCanvas(gridLayoutTargets[i], rootRtForMeasure, out float ow, out float oh);
                            maxW = Mathf.Max(maxW, ow);
                            maxH = Mathf.Max(maxH, oh);
                        }
                        maxW = Mathf.Max(1e-3f, maxW);
                        maxH = Mathf.Max(1e-3f, maxH);
                        FitAllButtonSizesInsideCanvas(gridLayoutTargets, rootRtForMeasure, maxW, maxH, out float fittedMaxW, out float fittedMaxH);
                        float gap = Mathf.Max(0f, fittsMinGap);
                        float rowGapY = gap + Mathf.Max(0f, fittsExtraRowGapY);
                        ApplyGridLayoutPositions(gridLayoutTargets, sizeSrc, fittedMaxW, fittedMaxH, gap, rowGapY, true);
                        Canvas.ForceUpdateCanvases();
                        ClampTargetsInsideCanvas(gridLayoutTargets, rootRtForMeasure, Mathf.Max(0f, fittsCanvasPadding));
                    }

                    ApplyFittsLawSizing(gridLayoutTargets, sizeSrc);
                    Canvas.ForceUpdateCanvases();
                    FitAllButtonSizesInsideCanvas(gridLayoutTargets, rootRtForMeasure, out float finalMaxW, out float finalMaxH);
                    float finalGap = Mathf.Max(0f, fittsMinGap);
                    float finalRowGapY = finalGap + Mathf.Max(0f, fittsExtraRowGapY);
                    ApplyGridLayoutPositions(gridLayoutTargets, sizeSrc, finalMaxW, finalMaxH, finalGap, finalRowGapY, true);
                    Canvas.ForceUpdateCanvases();
                    ClampTargetsInsideCanvas(gridLayoutTargets, rootRtForMeasure, Mathf.Max(0f, fittsCanvasPadding));
                }
                else
                {
                    if (autoApplyGridLayoutInPlay || centerEachRowHorizontally)
                    {
                        bool measured = fittsLawSizeFromMain && rowLayoutFromMeasuredExtents;
                        ApplyGridLayoutPositions(gridLayoutTargets, sizeSrc, null, null, null, null, measured);
                    }

                    if (fittsLawSizeFromMain)
                        ApplyFittsLawSizing(gridLayoutTargets, sizeSrc);
                    else
                        RestoreBaselinesFor(gridLayoutTargets);
                }

                if (autoApplyGridLayoutInPlay || centerEachRowHorizontally || fittsLawSizeFromMain)
                    Canvas.ForceUpdateCanvases();

                if (fittsReady && rootRtForMeasure != null)
                    RefreshFittsMetricsCache(gridLayoutTargets, rootRtForMeasure);
            }
        }

        if (experiment != null)
            experiment.RegisterNumberButtons(activeButtons);
        else
            Debug.LogError("[GridNumberButtonLayout] experiment (HickExperiment) reference is empty");
    }

    private int GetEffectiveColumns()
    {
        if (gridPattern == GridPattern.TwoRowsByFourColumns || gridPattern == GridPattern.FourColumnsUpToEight)
            return 4;
        return Mathf.Max(1, columns);
    }

    int GetFixedSlotCount(int activeCount)
    {
        bool fixedEightSlots =
            restrictLayoutToEightTargets ||
            gridPattern == GridPattern.TwoRowsByFourColumns ||
            gridPattern == GridPattern.FourColumnsUpToEight;

        if (fixedEightSlots)
            return 8;

        return Mathf.Max(0, activeCount);
    }

    /// <summary>8-slot (4+4) pattern, etc.: the list of Rects used for layout/Fitts/horizontal centering to keep them always in the same place regardless of N.</summary>
    bool UsesFixedEightSlotGrid()
    {
        return restrictLayoutToEightTargets ||
               gridPattern == GridPattern.TwoRowsByFourColumns ||
               gridPattern == GridPattern.FourColumnsUpToEight;
    }

    List<RectTransform> BuildGridLayoutTargetsForCurrentMode()
    {
        if (!UsesFixedEightSlotGrid())
            return new List<RectTransform>(activeButtons);

        const int maxSlot = 8;
        var map = new Dictionary<int, RectTransform>();
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform rt = preplacedButtons[i];
            if (rt == null) continue;
            NumberButton nb = rt.GetComponent<NumberButton>();
            if (nb != null && nb.Number >= 1 && nb.Number <= maxSlot)
                map[nb.Number] = rt;
        }

        var list = new List<RectTransform>(maxSlot);
        for (int n = 1; n <= maxSlot; n++)
        {
            if (map.TryGetValue(n, out RectTransform rt))
                list.Add(rt);
        }

        if (list.Count != maxSlot)
            Debug.LogWarning($"[GridNumberButtonLayout] In fixed 8-slot mode but only found {list.Count} of the logical 1–8 buttons. Check the Inspector preplacedButtons and NumberButton numbers.");

        return list.Count > 0 ? list : new List<RectTransform>(activeButtons);
    }

    void RestoreBaselinesFor(IReadOnlyList<RectTransform> targets)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Count; i++)
        {
            var rt = targets[i];
            if (rt == null) continue;
            if (baselineSizeDelta.TryGetValue(rt, out Vector2 sd))
                rt.sizeDelta = sd;
            if (baselineLocalRotation.TryGetValue(rt, out Quaternion rot))
                rt.localRotation = rot;
            RestoreLabelBaseline(rt);
        }
    }

    void ApplyFittsLawSizing(IReadOnlyList<RectTransform> targets, RectTransform sizeSource)
    {
        if (targets == null || targets.Count == 0 || sizeSource == null) return;
        if (!fittsLawSizeFromMain || mainButtonReference == null)
        {
            RestoreBaselinesFor(targets);
            return;
        }

        Canvas mainCanvas = mainButtonReference.GetComponentInParent<Canvas>();
        if (mainCanvas == null)
        {
            Debug.LogWarning("[GridNumberButtonLayout] mainButtonReference has no Canvas parent.");
            RestoreBaselinesFor(targets);
            return;
        }

        RectTransform rootRt = mainCanvas.rootCanvas.transform as RectTransform;
        if (rootRt == null)
        {
            RestoreBaselinesFor(targets);
            return;
        }

        GetLayoutCellSize(sizeSource, out float bw, out float bh);
        Vector2 baseSize = new Vector2(Mathf.Max(1e-3f, bw), Mathf.Max(1e-3f, bh));
        float baseSizePx = Mathf.Max(baseSize.x, baseSize.y);

        Vector2 mainLocal = WorldToRootCanvasLocal(rootRt, GetRectWorldCenter(mainButtonReference));
        float refH = Mathf.Max(1e-3f, fittsReferenceResolution.y);
        float rootH = Mathf.Max(1e-3f, rootRt.rect.height);
        float canvasUnitsPerRefPx = rootH / refH;
        float targetIdBits = FittsLawModel.ResolveTargetIdBits(
            fittsTargetIdBits, fittsReferenceRadiusPx, baseSizePx, pixelsPerMm);

        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;

            Vector2 btnLocal = WorldToRootCanvasLocal(rootRt, GetRectWorldCenter(rt));
            Vector2 delta = btnLocal - mainLocal;
            float dCanvas = delta.magnitude;
            float dPx = dCanvas / Mathf.Max(1e-6f, canvasUnitsPerRefPx);

            float angle = Mathf.Atan2(delta.y, delta.x);
            float angularBoost = 1f + fittsAngularWeight * Mathf.Abs(Mathf.Sin(angle));

            float radialScale = ComputeFittsRadialScale(dPx, baseSizePx, targetIdBits);
            float visualScale = radialScale * angularBoost;
            Vector2 newSize = baseSize * visualScale;

            // Square it: based on the shorter side (= the graphic size actually visible with preserveAspect)
            if (fittsSquareButtons)
            {
                float side = Mathf.Min(newSize.x, newSize.y);
                newSize = new Vector2(side, side);
            }

            // Guarantee minimum physical size (mm): even the smallest button won't go below this size
            float minSide = MmToCanvasUnits(fittsMinButtonSizeMm, rootRt);
            if (minSide > 0f)
                newSize = new Vector2(Mathf.Max(newSize.x, minSide), Mathf.Max(newSize.y, minSide));

            rt.sizeDelta = newSize;
            ApplyLabelScaleFromBaseline(rt, visualScale);

            if (orientButtonsTowardMain)
            {
                float deg = angle * Mathf.Rad2Deg;
                rt.localRotation = Quaternion.Euler(0f, 0f, deg - 90f);
            }
            else if (baselineLocalRotation.TryGetValue(rt, out Quaternion brot))
                rt.localRotation = brot;
        }
    }

    /// <summary>Device physical pixel density (px/mm). Computed from the actual resolution + diagonal rather than trusting Screen.dpi.</summary>
    float DevicePixelsPerMm()
    {
        if (physicalDiagonalMm > 1f)
        {
            float w = Screen.width;
            float h = Screen.height;
            float diagPx = Mathf.Sqrt(w * w + h * h);
            if (diagPx > 1f) return diagPx / physicalDiagonalMm;
        }
        float dpi = Screen.dpi;
        if (dpi < 1f) dpi = 160f;
        return dpi / 25.4f;
    }

    /// <summary>Physical mm → canvas local units (the value used directly for sizeDelta).</summary>
    float MmToCanvasUnits(float mm, RectTransform rootRt)
    {
        if (mm <= 0f) return 0f;
        float physicalPx = mm * DevicePixelsPerMm();
        float scale = 1f;
        if (rootRt != null)
        {
            Canvas c = rootRt.GetComponent<Canvas>();
            if (c != null && c.scaleFactor > 1e-6f) scale = c.scaleFactor;
        }
        return physicalPx / Mathf.Max(scale, 1e-6f);
    }

    float ComputeFittsRadialScale(float amplitudePx, float baseSizePx, float targetIdBits)
    {
        if (fittsSizingModel == FittsSizingModel.LegacyLinear)
        {
            float radialScale = amplitudePx / Mathf.Max(1e-3f, fittsReferenceRadiusPx);
            return Mathf.Clamp(radialScale, fittsMinScale, fittsMaxScale);
        }

        float amplitudeMm = FittsLawModel.PxToMm(amplitudePx, pixelsPerMm);
        float widthMm = FittsLawModel.WidthForIsoId(
            amplitudeMm, targetIdBits, fittsSizingModel, fittsAmbiguityC_mm);
        float widthPx = FittsLawModel.MmToPx(widthMm, pixelsPerMm);
        float scale = widthPx / Mathf.Max(1e-3f, baseSizePx);
        return Mathf.Clamp(scale, fittsMinScale, fittsMaxScale);
    }

    void RefreshFittsMetricsCache(IReadOnlyList<RectTransform> targets, RectTransform rootRt)
    {
        fittsMetricsByLogicalNumber.Clear();
        if (targets == null || rootRt == null || mainButtonReference == null) return;

        float refH = Mathf.Max(1e-3f, fittsReferenceResolution.y);
        float rootH = Mathf.Max(1e-3f, rootRt.rect.height);
        float canvasUnitsPerRefPx = rootH / refH;
        Vector2 mainLocal = WorldToRootCanvasLocal(rootRt, GetRectWorldCenter(mainButtonReference));
        string modelKey = FittsLawModel.ModelKey(fittsSizingModel);

        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;

            NumberButton nb = rt.GetComponent<NumberButton>();
            if (nb == null || nb.Number < 1) continue;

            Vector2 btnLocal = WorldToRootCanvasLocal(rootRt, GetRectWorldCenter(rt));
            float dPx = (btnLocal - mainLocal).magnitude / Mathf.Max(1e-6f, canvasUnitsPerRefPx);
            float amplitudeMm = FittsLawModel.PxToMm(dPx, pixelsPerMm);

            GetAxisAlignedSizeInRootCanvas(rt, rootRt, out float wCanvas, out float hCanvas);
            float widthPx = Mathf.Max(wCanvas, hCanvas) / Mathf.Max(1e-6f, canvasUnitsPerRefPx);
            float widthMm = FittsLawModel.PxToMm(widthPx, pixelsPerMm);

            fittsMetricsByLogicalNumber[nb.Number] = new FittsMetrics
            {
                amplitudeMm = amplitudeMm,
                widthMm = widthMm,
                idBaseline = FittsLawModel.ShannonId(amplitudeMm, widthMm),
                idFFitts = FittsLawModel.FFittsIdSqrt(amplitudeMm, widthMm, fittsAmbiguityC_mm),
                fittsModel = modelKey,
            };
        }
    }

    static Vector2 WorldToRootCanvasLocal(RectTransform rootCanvasRect, Vector3 worldPos)
    {
        Vector3 local = rootCanvasRect.InverseTransformPoint(worldPos);
        return new Vector2(local.x, local.y);
    }

    /// <summary>The axis-aligned bounding size in the root canvas local XY plane, including rotated UI (for anti-overlap spacing).</summary>
    static void GetAxisAlignedSizeInRootCanvas(RectTransform target, RectTransform rootCanvasRect, out float width, out float height)
    {
        width = 0f;
        height = 0f;
        if (target == null || rootCanvasRect == null) return;

        target.GetWorldCorners(CornerScratch);
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = WorldToRootCanvasLocal(rootCanvasRect, CornerScratch[i]);
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        width = Mathf.Max(0f, maxX - minX);
        height = Mathf.Max(0f, maxY - minY);
    }

    void GetLayoutCellSize(RectTransform src, out float w, out float h)
    {
        w = 0f;
        h = 0f;
        if (src == null) return;
        if (baselineSizeDelta.TryGetValue(src, out Vector2 sd) && sd.sqrMagnitude > 1e-6f)
        {
            w = sd.x;
            h = sd.y;
            return;
        }

        w = src.rect.width;
        h = src.rect.height;
    }

    /// <summary>Rows increase from top (0) → down. Columns go from left (0) → right.</summary>
    private void ApplyGridLayoutPositions(IReadOnlyList<RectTransform> targets, RectTransform prefabSizeSource)
    {
        ApplyGridLayoutPositions(targets, prefabSizeSource, null, null, null, null, false);
    }

    /// <param name="overrideCellW">If null, the prefab baseline width. Pass the largest button width when preventing Fitts overlap.</param>
    /// <param name="overrideCellH">If null, the prefab baseline height.</param>
    /// <param name="layoutFromMeasuredExtents">If on, stacks rows using each button's axis-aligned size relative to the parent, and aligns horizontal/vertical centers per row (Fitts).</param>
    private void ApplyGridLayoutPositions(IReadOnlyList<RectTransform> targets, RectTransform prefabSizeSource, float? overrideCellW, float? overrideCellH, float? overrideSpacingX, float? overrideSpacingY, bool layoutFromMeasuredExtents = false)
    {
        if (targets == null || targets.Count == 0 || prefabSizeSource == null) return;

        GetLayoutCellSize(prefabSizeSource, out float baseW, out float baseH);
        float w = overrideCellW ?? baseW;
        float h = overrideCellH ?? baseH;
        float sx = overrideSpacingX ?? spacingX;
        float sy = overrideSpacingY ?? spacingY;
        w = Mathf.Max(1e-3f, w);
        h = Mathf.Max(1e-3f, h);
        int cols = GetEffectiveColumns();
        int count = targets.Count;
        int slotCount = Mathf.Max(count, GetFixedSlotCount(count));
        int rows = cols > 0 ? Mathf.CeilToInt(slotCount / (float)cols) : 0;

        RectTransform parentRt = targets[0] != null ? targets[0].parent as RectTransform : null;
        bool useMeasured = layoutFromMeasuredExtents && parentRt != null && rowLayoutFromMeasuredExtents;

        if (useMeasured)
        {
            ApplyGridLayoutPositionsMeasured(targets, parentRt, w, h, sx, sy, cols, count, rows);
        }
        else
        {
            ApplyGridLayoutPositionsUniform(targets, w, h, sx, sy, cols, count, rows, slotCount);
            // Vertically center-align so that even in a uniform grid each button sits on the row centerline (so it isn't top-aligned even when the pivot isn't centered)
            AlignRowCentersVerticallyInWorldY(targets, cols, count);
        }

        if (alignGridToCanvasCenter)
            AlignGridCentroidToRootCanvas(targets);

        if (alignGridHorizontalBoundsToRootCanvas)
            AlignGridHorizontalBoundsToRootCanvas(targets);
    }

    void ApplyGridLayoutPositionsUniform(IReadOnlyList<RectTransform> targets, float w, float h, float sx, float sy, int cols, int count, int rows, int slotCount)
    {
        float gridW = cols * w + (cols - 1) * sx;
        float gridH = rows * h + (rows - 1) * sy;
        float shiftX = centerGridInParent ? (-gridW * 0.5f + w * 0.5f) : 0f;
        float shiftY = centerGridInParent ? (gridH * 0.5f - h * 0.5f) : 0f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;

            int col = i % cols;
            int row = i / cols;

            float rowOffsetX = 0f;
            if (centerEachRowHorizontally)
            {
                int firstInRow = row * cols;
                int itemsInRow = Mathf.Min(cols, count - firstInRow);
                if (itemsInRow > 0)
                {
                    float fullRowW = cols * w + (cols - 1) * sx;
                    float rowW = itemsInRow * w + (itemsInRow - 1) * sx;
                    if (rowW < fullRowW - 1e-4f)
                        rowOffsetX = (fullRowW - rowW) * 0.5f;
                }
            }

            float x = startOffset.x + rowOffsetX + col * (w + sx) + shiftX;
            float y = startOffset.y + row * (h + sy);
            rt.anchoredPosition = new Vector2(x, -y + shiftY);
        }
    }

    /// <summary>Variable size such as Fitts: the row reference width is the slot grid (cols×w), horizontally centered to the sum of actual widths within the row. Vertically, stacks by each row's max height then center-aligns by world Y.</summary>
    void ApplyGridLayoutPositionsMeasured(IReadOnlyList<RectTransform> targets, RectTransform parentRt, float w, float h, float sx, float sy, int cols, int count, int rows)
    {
        float refRowW = cols * w + (cols - 1) * sx;

        var lw = new float[count];
        var lh = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (targets[i] == null) continue;
            GetAxisAlignedExtentsInParent(targets[i], parentRt, out lw[i], out lh[i]);
        }

        var rowMaxH = new float[rows];
        for (int r = 0; r < rows; r++)
        {
            int first = r * cols;
            int n = Mathf.Min(cols, Mathf.Max(0, count - first));
            float rm = h;
            if (n > 0)
            {
                rm = 0f;
                for (int k = 0; k < n; k++)
                    rm = Mathf.Max(rm, lh[first + k]);
            }
            rowMaxH[r] = Mathf.Max(1e-3f, rm);
        }

        float gridH = 0f;
        for (int r = 0; r < rows; r++)
        {
            gridH += rowMaxH[r];
            if (r < rows - 1) gridH += sy;
        }

        float gridW = refRowW;
        float shiftX = centerGridInParent ? (-gridW * 0.5f + w * 0.5f) : 0f;
        float shiftY = centerGridInParent ? (gridH * 0.5f - h * 0.5f) : 0f;

        float rowBandTop = startOffset.y;
        for (int r = 0; r < rows; r++)
        {
            int first = r * cols;
            int n = Mathf.Min(cols, Mathf.Max(0, count - first));
            if (n <= 0)
            {
                rowBandTop += rowMaxH[r] + sy;
                continue;
            }

            float rowContentW = 0f;
            for (int k = 0; k < n; k++)
                rowContentW += lw[first + k];
            if (n > 1) rowContentW += (n - 1) * sx;

            float rowOffsetX = 0f;
            if (centerEachRowHorizontally && rowContentW < refRowW - 1e-4f)
                rowOffsetX = (refRowW - rowContentW) * 0.5f;

            float cursorLeft = startOffset.x + shiftX + rowOffsetX;
            float cum = 0f;
            for (int k = 0; k < n; k++)
            {
                int i = first + k;
                RectTransform rt = targets[i];
                if (rt == null) continue;

                float cx = cursorLeft + cum + lw[i] * 0.5f;
                float topY = rowBandTop;
                rt.anchoredPosition = new Vector2(cx, -topY + shiftY);
                cum += lw[i] + sx;
            }

            rowBandTop += rowMaxH[r] + sy;
        }

        AlignRowCentersVerticallyInWorldY(targets, cols, count);
    }

    static void GetAxisAlignedExtentsInParent(RectTransform rt, RectTransform parent, out float width, out float height)
    {
        width = 1e-3f;
        height = 1e-3f;
        if (rt == null || parent == null) return;

        rt.GetWorldCorners(CornerScratch);
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int c = 0; c < 4; c++)
        {
            Vector3 lp = parent.InverseTransformPoint(CornerScratch[c]);
            minX = Mathf.Min(minX, lp.x);
            maxX = Mathf.Max(maxX, lp.x);
            minY = Mathf.Min(minY, lp.y);
            maxY = Mathf.Max(maxY, lp.y);
        }

        width = Mathf.Max(1e-3f, maxX - minX);
        height = Mathf.Max(1e-3f, maxY - minY);
    }

    static void AlignRowCentersVerticallyInWorldY(IReadOnlyList<RectTransform> targets, int cols, int count)
    {
        if (targets == null || cols <= 0 || count <= 0) return;

        int maxRow = (count - 1) / cols;
        for (int r = 0; r <= maxRow; r++)
        {
            int first = r * cols;
            int n = Mathf.Min(cols, count - first);
            if (n <= 0) continue;

            float sumY = 0f;
            int nOk = 0;
            for (int k = 0; k < n; k++)
            {
                RectTransform rt = targets[first + k];
                if (rt == null) continue;
                sumY += GetRectWorldCenter(rt).y;
                nOk++;
            }
            if (nOk == 0) continue;

            float meanY = sumY / nOk;
            for (int k = 0; k < n; k++)
            {
                RectTransform rt = targets[first + k];
                if (rt == null) continue;
                float cy = GetRectWorldCenter(rt).y;
                Vector3 p = rt.position;
                p.y += meanY - cy;
                rt.position = p;
            }
        }
    }

    private static Vector3 GetRectWorldCenter(RectTransform rt)
    {
        if (rt == null) return Vector3.zero;
        rt.GetWorldCorners(CornerScratch);
        return (CornerScratch[0] + CornerScratch[1] + CornerScratch[2] + CornerScratch[3]) * 0.25f;
    }

    /// <summary>Aligns the buttons' world centroid to the center of the rootCanvas rectangle (same delta applied to each RectTransform.position).</summary>
    private void AlignGridCentroidToRootCanvas(IReadOnlyList<RectTransform> targets)
    {
        if (targets == null || targets.Count == 0) return;

        Canvas canvas = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null)
            {
                canvas = targets[i].GetComponentInParent<Canvas>();
                break;
            }
        }
        if (canvas == null) return;

        RectTransform rootRt = canvas.rootCanvas.transform as RectTransform;
        if (rootRt == null) return;

        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            sum += GetRectWorldCenter(targets[i]);
            n++;
        }
        if (n == 0) return;

        Vector3 gridCentroid = sum / n;
        Vector3 canvasCenter = GetRectWorldCenter(rootRt);
        Vector3 delta = canvasCenter - gridCentroid;

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            targets[i].position += delta;
        }
    }

    /// <summary>Aligns the horizontal center of the axis-aligned AABB of all active buttons to root canvas local X=0 (unlike the centroid, the left/right margins are symmetric).</summary>
    void AlignGridHorizontalBoundsToRootCanvas(IReadOnlyList<RectTransform> targets)
    {
        if (targets == null || targets.Count == 0) return;

        Canvas canvas = null;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null)
            {
                canvas = targets[i].GetComponentInParent<Canvas>();
                break;
            }
        }
        if (canvas == null) return;

        RectTransform rootRt = canvas.rootCanvas.transform as RectTransform;
        if (rootRt == null) return;

        GetTargetsBoundsInRootCanvas(targets, rootRt, out float minX, out float maxX, out _, out _);
        if (float.IsInfinity(minX) || float.IsInfinity(maxX)) return;

        float midX = 0.5f * (minX + maxX);
        MoveTargetsInRootCanvas(targets, rootRt, -midX, 0f);
    }

    [ContextMenu("Apply Grid Layout (Preplaced Only)")]
    private void ContextApplyGridLayout()
    {
        if (preplacedButtons.Count == 0)
        {
            Debug.LogWarning("[GridNumberButtonLayout] preplacedButtons is empty");
            return;
        }
        RectTransform sizeSrc = buttonPrefab != null ? buttonPrefab : preplacedButtons[0];
        ApplyGridLayoutPositions(preplacedButtons, sizeSrc, null, null, null, null, false);
        Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }

    [ContextMenu("Bake Preplaced Buttons (Max Count)")]
    private void BakePreplacedButtons()
    {
        if (buttonPrefab == null)
        {
            Debug.LogError("[GridNumberButtonLayout] buttonPrefab is empty");
            return;
        }

        Transform parent = spawnParent != null ? spawnParent : transform;
        if (parent == null)
        {
            Debug.LogError("[GridNumberButtonLayout] spawnParent is empty");
            return;
        }

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

        Canvas.ForceUpdateCanvases();
        ApplyGridLayoutPositions(preplacedButtons, buttonPrefab, null, null, null, null, false);
        CacheBaselinesForPreplaced();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
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

    void FitAllButtonSizesInsideCanvas(IReadOnlyList<RectTransform> targets, RectTransform rootRt, out float fittedMaxW, out float fittedMaxH)
    {
        FitAllButtonSizesInsideCanvas(targets, rootRt, 0f, 0f, out fittedMaxW, out fittedMaxH);
    }

    void FitAllButtonSizesInsideCanvas(IReadOnlyList<RectTransform> targets, RectTransform rootRt, float maxW, float maxH, out float fittedMaxW, out float fittedMaxH)
    {
        fittedMaxW = Mathf.Max(1e-3f, maxW);
        fittedMaxH = Mathf.Max(1e-3f, maxH);
        if (targets == null || targets.Count == 0 || rootRt == null) return;

        if (maxW <= 0f || maxH <= 0f)
        {
            maxW = 0f;
            maxH = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                GetAxisAlignedSizeInRootCanvas(targets[i], rootRt, out float ow, out float oh);
                maxW = Mathf.Max(maxW, ow);
                maxH = Mathf.Max(maxH, oh);
            }
            maxW = Mathf.Max(1e-3f, maxW);
            maxH = Mathf.Max(1e-3f, maxH);
        }

        int cols = Mathf.Max(1, GetEffectiveColumns());
        int slotCount = Mathf.Max(targets.Count, GetFixedSlotCount(targets.Count));
        int rows = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)cols));
        float gap = Mathf.Max(0f, fittsMinGap);
        float pad = Mathf.Max(0f, fittsCanvasPadding);

        float vGap = gap + Mathf.Max(0f, fittsExtraRowGapY);
        float availableW = Mathf.Max(1e-3f, rootRt.rect.width - 2f * pad - (cols - 1) * gap);
        float availableH = Mathf.Max(1e-3f, rootRt.rect.height - 2f * pad - (rows - 1) * vGap);
        float scaleW = availableW / (cols * maxW);
        float scaleH = availableH / (rows * maxH);
        float fitScale = Mathf.Min(1f, scaleW, scaleH);
        fitScale = Mathf.Clamp(fitScale, 0.05f, 1f);

        if (fitScale < 0.9999f)
        {
            // Keep a lower bound so the fit-to-screen shrink doesn't go below the minimum physical size (mm).
            float minSide = MmToCanvasUnits(fittsMinButtonSizeMm, rootRt);
            bool clampedToMin = false;
            for (int i = 0; i < targets.Count; i++)
            {
                RectTransform rt = targets[i];
                if (rt == null) continue;
                rt.sizeDelta *= fitScale;
                if (minSide > 0f && (rt.sizeDelta.x < minSide || rt.sizeDelta.y < minSide))
                {
                    rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, minSide), Mathf.Max(rt.sizeDelta.y, minSide));
                    clampedToMin = true;
                }
                ApplyLabelScaleMultiply(rt, fitScale);
            }
            if (clampedToMin)
                Debug.LogWarning($"[GridNumberButtonLayout] Limited the fit-to-screen shrink to maintain the minimum button size ({fittsMinButtonSizeMm}mm). Buttons may overflow the screen or overlap, so check the spacing and count.");
            maxW = Mathf.Max(maxW * fitScale, minSide);
            maxH = Mathf.Max(maxH * fitScale, minSide);
        }

        fittedMaxW = Mathf.Max(1e-3f, maxW);
        fittedMaxH = Mathf.Max(1e-3f, maxH);
    }

    static void GetTargetsBoundsInRootCanvas(IReadOnlyList<RectTransform> targets, RectTransform rootRt, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        if (targets == null || rootRt == null) return;

        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;
            rt.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector2 p = WorldToRootCanvasLocal(rootRt, CornerScratch[c]);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
        }
    }

    static void MoveTargetsInRootCanvas(IReadOnlyList<RectTransform> targets, RectTransform rootRt, float dx, float dy)
    {
        if (targets == null || rootRt == null) return;
        Vector3 worldDelta = rootRt.TransformVector(new Vector3(dx, dy, 0f));
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            targets[i].position += worldDelta;
        }
    }

    static void ClampTargetsInsideCanvas(IReadOnlyList<RectTransform> targets, RectTransform rootRt, float padding)
    {
        if (targets == null || targets.Count == 0 || rootRt == null) return;
        GetTargetsBoundsInRootCanvas(targets, rootRt, out float minX, out float maxX, out float minY, out float maxY);
        if (float.IsInfinity(minX) || float.IsInfinity(maxX) || float.IsInfinity(minY) || float.IsInfinity(maxY)) return;

        float left = -rootRt.rect.width * 0.5f + padding;
        float right = rootRt.rect.width * 0.5f - padding;
        float bottom = -rootRt.rect.height * 0.5f + padding;
        float top = rootRt.rect.height * 0.5f - padding;

        float dx = 0f;
        if (minX < left) dx = left - minX;
        else if (maxX > right) dx = right - maxX;

        float dy = 0f;
        if (minY < bottom) dy = bottom - minY;
        else if (maxY > top) dy = top - maxY;

        if (Mathf.Abs(dx) > 1e-4f || Mathf.Abs(dy) > 1e-4f)
            MoveTargetsInRootCanvas(targets, rootRt, dx, dy);
    }

    void CacheLabelBaselines(RectTransform root)
    {
        if (root == null) return;

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text t = texts[i];
            if (t == null) continue;
            if (!baselineLegacyFontSize.ContainsKey(t))
                baselineLegacyFontSize[t] = t.fontSize;
        }

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TMP_Text t = tmps[i];
            if (t == null) continue;
            if (!baselineTmpFontSize.ContainsKey(t))
                baselineTmpFontSize[t] = t.fontSize;
        }
    }

    void RestoreLabelBaseline(RectTransform root)
    {
        if (!scaleLabelWithButton || root == null) return;

        CacheLabelBaselines(root);

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text t = texts[i];
            if (t == null) continue;
            if (baselineLegacyFontSize.TryGetValue(t, out int baseSize))
                t.fontSize = baseSize;
        }

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TMP_Text t = tmps[i];
            if (t == null) continue;
            if (baselineTmpFontSize.TryGetValue(t, out float baseSize))
                t.fontSize = baseSize;
        }
    }

    void ApplyLabelScaleFromBaseline(RectTransform root, float scale)
    {
        if (!scaleLabelWithButton || root == null) return;
        scale = Mathf.Max(0.05f, scale);
        CacheLabelBaselines(root);

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text t = texts[i];
            if (t == null) continue;
            if (baselineLegacyFontSize.TryGetValue(t, out int baseSize))
                t.fontSize = Mathf.Max(1, Mathf.RoundToInt(baseSize * scale));
        }

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TMP_Text t = tmps[i];
            if (t == null) continue;
            if (baselineTmpFontSize.TryGetValue(t, out float baseSize))
                t.fontSize = Mathf.Max(1f, baseSize * scale);
        }
    }

    void ApplyLabelScaleMultiply(RectTransform root, float factor)
    {
        if (!scaleLabelWithButton || root == null) return;
        factor = Mathf.Max(0.05f, factor);

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text t = texts[i];
            if (t == null) continue;
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(t.fontSize * factor));
        }

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TMP_Text t = tmps[i];
            if (t == null) continue;
            t.fontSize = Mathf.Max(1f, t.fontSize * factor);
        }
    }
}