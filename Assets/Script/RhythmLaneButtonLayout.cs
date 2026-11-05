using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A spawner that arranges number buttons in a single horizontal row (rhythm-game lanes) at the bottom of the screen.
/// Like DJMAX/jubeat, keys 1~8 line up in a row, and you press the lane corresponding to the target number shown above the center.
/// The experiment format (stimulus->response->RT measurement->log2(N+1) regression) reuses <see cref="HickExperiment"/> as-is,
/// and the maximum N is limited to 8 (basic Hick's law, N=1..8).
/// </summary>
public class RhythmLaneButtonLayout : NumberButtonSpawnerBase
{
    public const int MaxLanes = 8;

    [Header("References")]
    [Tooltip("Number buttons placed in the hierarchy (in order, starting from 1). NumberButton.logicalNumber determines the lane number. At runtime, only the buttons connected here are toggled on/off and positioned; none are newly created. You can also create them all at once with Bake below.")]
    [SerializeField] private List<RectTransform> preplacedButtons = new();

    [Header("Bake (editor only: creates buttons in the hierarchy)")]
    [Tooltip("The source button prefab/template to clone when baking. A button with a NumberButton and a number label (Text/TMP) is recommended.")]
    [SerializeField] private RectTransform buttonPrefab;
    [Tooltip("Parent the baked buttons go under (if empty, this object). Usually a RectTransform under the canvas.")]
    [SerializeField] private Transform spawnParent;
    [Tooltip("Number of buttons to create with Bake (up to 8 lanes)")]
    [SerializeField] private int bakeCount = MaxLanes;

    [Header("Lane Layout")]
    [Tooltip("If on, divides the full width of the screen (root canvas) into 8 and forces all buttons to the same width (full width divided into 8). If off, uses the Lane Cell Width Override / button baseline width below.")]
    [SerializeField] private bool fitLanesToFullWidth = true;
    [Tooltip("Left/right edge margin (px) when Fit Lanes To Full Width is on")]
    [SerializeField] private float edgeMargin = 0f;
    [Tooltip("If on, fills the button height to the full top-to-bottom of the screen (root canvas) and centers the row vertically on the screen. If off, uses button baseline height + Row Offset.y.")]
    [SerializeField] private bool fillFullHeight = true;
    [Tooltip("Top/bottom margin (px) when Fill Full Height is on")]
    [SerializeField] private float verticalMargin = 0f;
    [Tooltip("Horizontal spacing between lanes (px)")]
    [SerializeField] private float laneSpacing = 8f;
    [Tooltip("Used only when Fit Lanes To Full Width is off. If greater than 0, forces this value as the horizontal width of one lane; if 0, uses the button baseline width.")]
    [SerializeField] private float laneCellWidthOverride = 0f;
    [Tooltip("Reference-point offset (px) for the whole row. When Fill Full Height is off, y adjusts the row height (negative=down).")]
    [SerializeField] private Vector2 rowOffset = new Vector2(0f, 0f);
    [Tooltip("If on, keeps the 8-division slot positions even when N<8 (unused lane slots stay empty). If off, gathers the active N in the center (button size stays the 8-division size).")]
    [SerializeField] private bool keepFixedEightLaneSlots = false;
    [Tooltip("If on, aligns the horizontal center of the whole row (active button AABB) to the center of the root canvas (X=0).")]
    [SerializeField] private bool alignRowHorizontalBoundsToRootCanvas = true;
    [Tooltip("If on, recomputes the lane coordinates on runtime SetCount. If off, uses the coordinates placed in the editor as-is and only toggles active/inactive.")]
    [SerializeField] private bool applyLaneLayoutInPlay = true;
    [Tooltip("If on, hides all lane buttons at play start and shows them only when the experiment calls SetCount(N) (after passing the auto-mode intro). Prevents the issue where a fullscreen button covers the intro 'Continue' button and stalls it.")]
    [SerializeField] private bool hideButtonsUntilStart = true;

    [Header("Link")]
    [SerializeField] private HickExperiment experiment;

    private readonly List<RectTransform> activeButtons = new();
    private static readonly Vector3[] CornerScratch = new Vector3[4];
    private readonly Dictionary<RectTransform, Vector2> baselineSizeDelta = new();
    private int currentN;

#if UNITY_EDITOR
    private void OnValidate()
    {
        laneSpacing = Mathf.Max(0f, laneSpacing);
        laneCellWidthOverride = Mathf.Max(0f, laneCellWidthOverride);
        edgeMargin = Mathf.Max(0f, edgeMargin);
        verticalMargin = Mathf.Max(0f, verticalMargin);
        bakeCount = Mathf.Clamp(bakeCount, 0, MaxLanes);
    }
#endif

    private void Awake()
    {
        CacheBaselineSizes();
        // Hide all at start so the fullscreen lane buttons don't cover the auto-mode intro (Continue button).
        // When the experiment calls SetCount(N) -> ActivateForCount, buttons 1..N turn back on.
        if (hideButtonsUntilStart)
            SetAllPreplacedActive(false);
    }

    private void SetAllPreplacedActive(bool on)
    {
        if (preplacedButtons == null) return;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform rt = preplacedButtons[i];
            if (rt != null && rt.gameObject.activeSelf != on)
                rt.gameObject.SetActive(on);
        }
    }

    private void CacheBaselineSizes()
    {
        baselineSizeDelta.Clear();
        if (preplacedButtons == null) return;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform rt = preplacedButtons[i];
            if (rt == null) continue;
            baselineSizeDelta[rt] = rt.sizeDelta;
        }
    }

    public override void SetCount(int n)
    {
        currentN = Mathf.Clamp(n, 0, MaxLanes);
        ActivateForCount();
    }

    public override int GetMaxSupportedCount()
    {
        return Mathf.Min(MaxLanes, ComputePreplacedMaxLogicalOrCount());
    }

    private int ComputePreplacedMaxLogicalOrCount()
    {
        if (preplacedButtons == null || preplacedButtons.Count == 0) return 0;
        int maxSlot = 0;
        int count = 0;
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;
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
            Debug.LogError("[RhythmLaneButtonLayout] preplacedButtons is empty. Connect the buttons directly in the Inspector, in order starting from 1.");
            return;
        }
        if (preplacedButtons.Count < currentN)
            Debug.LogError($"[RhythmLaneButtonLayout] Not enough buttons: available={preplacedButtons.Count}, requestedN={currentN}");

        activeButtons.Clear();
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;

            var nb = button.GetComponent<NumberButton>();
            int slot = nb != null ? nb.Number : (i + 1);
            bool shouldShow = slot >= 1 && slot <= currentN;
            if (button.gameObject.activeSelf != shouldShow)
                button.gameObject.SetActive(shouldShow);
            if (shouldShow)
                activeButtons.Add(button);
        }

        activeButtons.Sort((a, b) =>
        {
            int na = a.GetComponent<NumberButton>()?.Number ?? 0;
            int nb = b.GetComponent<NumberButton>()?.Number ?? 0;
            int c = na.CompareTo(nb);
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });

        if (activeButtons.Count < currentN)
            Debug.LogWarning($"[RhythmLaneButtonLayout] Requested N={currentN}, but there are only {activeButtons.Count} active buttons with logicalNumber<=N.");

        if (Application.isPlaying && applyLaneLayoutInPlay)
            ApplyRowLayout(activeButtons);

        if (experiment != null)
            experiment.RegisterNumberButtons(activeButtons);
        else
            Debug.LogError("[RhythmLaneButtonLayout] experiment(HickExperiment) reference is empty");
    }

    /// <summary>Arranges the active buttons evenly in a single horizontal row (in lane-number order, left->right).</summary>
    private void ApplyRowLayout(List<RectTransform> targets)
    {
        if (targets == null || targets.Count == 0) return;

        RectTransform rootRt = GetRootCanvasRect(targets);

        float cellW = ResolveCellWidth(targets, rootRt);
        float step = cellW + laneSpacing;

        // Force button width (full width divided into 8) / height (fill top-to-bottom).
        bool fillHeight = fillFullHeight && rootRt != null;
        float fullH = fillHeight ? Mathf.Max(1e-3f, rootRt.rect.height - 2f * Mathf.Max(0f, verticalMargin)) : 0f;
        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;
            float w = fitLanesToFullWidth ? cellW : rt.sizeDelta.x;
            float h = fillHeight ? fullH : (fitLanesToFullWidth ? ResolveBaselineHeight(rt) : rt.sizeDelta.y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // Slot basis: with fixed 8 slots, always the corresponding slot based on 8 cells; otherwise center the active N.
        int slotCount = keepFixedEightLaneSlots ? MaxLanes : targets.Count;
        float centerIndex = (slotCount - 1) * 0.5f;

        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;

            int slotIndex = i;
            if (keepFixedEightLaneSlots)
            {
                var nb = rt.GetComponent<NumberButton>();
                // logicalNumber 1..8 -> slots 0..7
                slotIndex = (nb != null ? nb.Number : (i + 1)) - 1;
                slotIndex = Mathf.Clamp(slotIndex, 0, MaxLanes - 1);
            }

            float x = rowOffset.x + (slotIndex - centerIndex) * step;
            rt.anchoredPosition = new Vector2(x, rowOffset.y);
        }

        // Horizontal center to canvas center, and if Fill Full Height, vertical center to canvas center too.
        if (alignRowHorizontalBoundsToRootCanvas || fillHeight)
        {
            Canvas.ForceUpdateCanvases();
            AlignRowBoundsToRootCanvas(targets, rootRt, alignRowHorizontalBoundsToRootCanvas, fillHeight);
        }
    }

    /// <summary>If full width divided into 8, (canvas width - end margins - total spacing) / 8; otherwise the override/prefab width.</summary>
    private float ResolveCellWidth(List<RectTransform> targets, RectTransform rootRt)
    {
        if (fitLanesToFullWidth && rootRt != null)
        {
            float usableW = rootRt.rect.width - 2f * Mathf.Max(0f, edgeMargin) - laneSpacing * (MaxLanes - 1);
            float cell = usableW / MaxLanes;
            if (cell > 1e-3f) return cell;
        }

        if (laneCellWidthOverride > 0f) return laneCellWidthOverride;

        RectTransform src = targets.Count > 0 ? targets[0] : null;
        if (src != null)
        {
            if (baselineSizeDelta.TryGetValue(src, out Vector2 sd) && sd.x > 1e-3f)
                return sd.x;
            float w = src.rect.width;
            if (w > 1e-3f) return w;
        }
        return 120f;
    }

    private float ResolveBaselineHeight(RectTransform rt)
    {
        if (rt != null && baselineSizeDelta.TryGetValue(rt, out Vector2 sd) && sd.y > 1e-3f)
            return sd.y;
        return rt != null ? rt.rect.height : 120f;
    }

    private static RectTransform GetRootCanvasRect(List<RectTransform> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            Canvas canvas = targets[i].GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.rootCanvas.transform as RectTransform;
        }
        return null;
    }

    /// <summary>Aligns the axis-aligned AABB center of all active buttons to the root canvas center (X=0/Y=0) (selectable per axis).</summary>
    private void AlignRowBoundsToRootCanvas(List<RectTransform> targets, RectTransform rootRt, bool alignX, bool alignY)
    {
        if (targets == null || targets.Count == 0) return;
        if (!alignX && !alignY) return;
        if (rootRt == null) rootRt = GetRootCanvasRect(targets);
        if (rootRt == null) return;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < targets.Count; i++)
        {
            RectTransform rt = targets[i];
            if (rt == null) continue;
            rt.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = rootRt.InverseTransformPoint(CornerScratch[c]);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }
        }
        if (float.IsInfinity(minX) || float.IsInfinity(maxY)) return;

        float dx = alignX ? -0.5f * (minX + maxX) : 0f;
        float dy = alignY ? -0.5f * (minY + maxY) : 0f;
        Vector3 worldDelta = rootRt.TransformVector(new Vector3(dx, dy, 0f));
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            targets[i].position += worldDelta;
        }
    }

    [ContextMenu("Apply Lane Layout (Preplaced Only)")]
    private void ContextApplyLaneLayout()
    {
        if (preplacedButtons.Count == 0)
        {
            Debug.LogWarning("[RhythmLaneButtonLayout] preplacedButtons is empty");
            return;
        }

        var list = new List<RectTransform>(preplacedButtons.Count);
        for (int i = 0; i < preplacedButtons.Count; i++)
            if (preplacedButtons[i] != null) list.Add(preplacedButtons[i]);

        list.Sort((a, b) =>
        {
            int na = a.GetComponent<NumberButton>()?.Number ?? 0;
            int nb = b.GetComponent<NumberButton>()?.Number ?? 0;
            int c = na.CompareTo(nb);
            if (c != 0) return c;
            return string.CompareOrdinal(a.name, b.name);
        });

        CacheBaselineSizes();
        ApplyRowLayout(list);
        Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }

    /// <summary>
    /// In the editor, clones buttonPrefab 1..bakeCount times into the hierarchy,
    /// sets each button's NumberButton.logicalNumber and number label to 1..N, then arranges them in a row.
    /// The created buttons are automatically connected to preplacedButtons (none are newly created at runtime).
    /// </summary>
    [ContextMenu("Bake Lane Buttons (1..N)")]
    private void BakeLaneButtons()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("[RhythmLaneButtonLayout] Run Bake in edit mode (play stopped).");
            return;
        }
        if (buttonPrefab == null)
        {
            Debug.LogError("[RhythmLaneButtonLayout] buttonPrefab is empty. Connect the source button to bake.");
            return;
        }

        Transform parent = spawnParent != null ? spawnParent : transform;
        ClearLaneButtons();
        preplacedButtons = new List<RectTransform>();

        int count = Mathf.Clamp(bakeCount, 1, MaxLanes);
        for (int i = 0; i < count; i++)
        {
            int logical = i + 1;
            Object inst = PrefabUtility.InstantiatePrefab(buttonPrefab.gameObject, parent);
            GameObject go = inst as GameObject;
            RectTransform clone = go != null ? go.GetComponent<RectTransform>() : null;
            if (clone == null) clone = Instantiate(buttonPrefab, parent);
            clone.name = $"Lane_{logical}";
            clone.gameObject.SetActive(true);

            NumberButton nb = clone.GetComponent<NumberButton>();
            if (nb == null) nb = clone.gameObject.AddComponent<NumberButton>();
            var so = new SerializedObject(nb);
            var prop = so.FindProperty("logicalNumber");
            if (prop != null)
            {
                prop.intValue = logical;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            SetLabelNumber(clone, logical.ToString());

            Undo.RegisterCreatedObjectUndo(clone.gameObject, "Bake Lane Buttons");
            preplacedButtons.Add(clone);
        }

        Canvas.ForceUpdateCanvases();
        CacheBaselineSizes();
        ApplyRowLayout(preplacedButtons);
        Canvas.ForceUpdateCanvases();
        EditorUtility.SetDirty(gameObject);
        Debug.Log($"[RhythmLaneButtonLayout] Bake complete: created {count} lane buttons (logicalNumber 1..{count}).");
#else
        Debug.LogWarning("[RhythmLaneButtonLayout] Bake only works in the editor.");
#endif
    }

    [ContextMenu("Clear Lane Buttons")]
    private void ClearLaneButtons()
    {
        for (int i = 0; i < preplacedButtons.Count; i++)
        {
            RectTransform button = preplacedButtons[i];
            if (button == null) continue;
            if (Application.isPlaying) Destroy(button.gameObject);
            else DestroyImmediate(button.gameObject);
        }
        preplacedButtons.Clear();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }

    /// <summary>Sets the number label (Legacy Text / TMP) text inside the button (for showing 1..N when baking).</summary>
    private static void SetLabelNumber(RectTransform root, string text)
    {
        if (root == null) return;

        Text[] texts = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
            if (texts[i] != null) texts[i].text = text;

        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
            if (tmps[i] != null) tmps[i].text = text;
    }
}
