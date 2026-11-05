using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Firebase.Firestore;

public class HickExperiment : MonoBehaviour
{
    private enum StartMode
    {
        HoldMainButton,
        DirectSelect
    }

    [Header("UI")]
    [SerializeField] private Button mainButton;
    [SerializeField] private Text targetText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text resultText;
    [SerializeField] private GameObject resultPanel;
    [Header("Graph")]
    [SerializeField] private RawImage hickGraphRawImage;
    [SerializeField] private int graphWidth = 640;
    [SerializeField] private int graphHeight = 360;
    [Tooltip("Optional: UI Text to place below/beside the graph (shows labels and axis names)")]
    [SerializeField] private Text graphTitleText;
    [SerializeField] private Text graphXAxisLabel;
    [SerializeField] private Text graphYAxisLabel;
    
    [Header("Web Report")]
    [SerializeField] private ResultReportLauncher reportLauncher;

    [Header("Graph Export")]
    [SerializeField] private bool saveGraphImageOnComplete = true;
    [SerializeField] private string graphImageFilePrefix = "hick_graph";

    [Header("Spawner (Grid or SemiCircle)")]
    [SerializeField] private NumberButtonSpawnerBase spawner;  // ✅ Plug in a different spawner per scene

    [Header("Experiment")]
    [SerializeField] private StartMode startMode = StartMode.DirectSelect;
    [SerializeField] private bool requirePressAllButtonsToStart = true;
    [SerializeField] private int minN = 1;
    [SerializeField] private int maxN = 10;
    [SerializeField] private int trialsPerN = 10;
    [SerializeField] private float minDelay = 1.0f;
    [SerializeField] private float maxDelay = 3.0f;
    [Tooltip("When on, number button clicks are disabled; selection only via swipe (hold the origin and drag in a direction)")]
    [SerializeField] private bool useSwipeOnly = true;

    [Header("Practice (practice before the main session)")]
    [Tooltip("Number of practice trials before the main session starts. Practice results are not saved.")]
    [SerializeField] private int practiceTrials = 3;

    [Header("Start Panel (start screen after practice is complete)")]
    [Tooltip("If you connect the existing scene's 'Start' panel, it will be reused. If empty, one is auto-generated at runtime (legacy behavior).")]
    [SerializeField] private GameObject startPanelOverride;
    [Tooltip("Optional: the guidance message Text of the reused panel. If empty, the message is left untouched.")]
    [SerializeField] private Text startPanelMessageOverride;
    [Tooltip("Optional: the start button of the reused panel. If empty, a Button is auto-detected among the panel's children and connected.")]
    [SerializeField] private Button startButtonOverride;

    [SerializeField] private RadialSwipeSelector thumbSelector;

    private bool ready = false;
    private bool isHoldingMain = false;
    private bool waitingForResponse = false;
    private Coroutine directSelectRoutine;
    private bool waitingForWarmupPress = false;
    private bool warmupCompletedForExperiment = false;
    private readonly HashSet<int> warmupPressedNumbers = new();

    // Practice (pre-main-session) state
    private bool inPractice = false;
    private int practiceDone = 0;
    private int practiceN = 0;
    private Coroutine practiceRoutine;
    private GameObject startPanel;
    private Text startPanelMessage;
    private readonly List<Dictionary<string, object>> practiceDocs = new();

    private List<int> nOrder;
    private int blockIndex = 0;
    private int trialInBlock = 0;

    // ✅ Target order for the current block (pre-generated so 1~N are evenly distributed, consumed by trialInBlock)
    private readonly List<int> blockTargetSequence = new();

    // ✅ Numbers already re-presented once in this block (each number is re-presented only once)
    private readonly HashSet<int> retriedThisBlock = new();

    private int N = 8;
    private int currentTarget = 0;
    private float stimulusTime = 0f;
    // Moment the stimulus was presented (monotonic clock, seconds). Reference point when decomposing RT into decision/movement time.
    private double stimulusOnsetTs = 0.0;
    // HoldMainButton+tap mode: the moment the finger is lifted off the main button (movement onset). Only the first release after the stimulus is recorded. <0 if not captured.
    private double mainReleaseTs = -1.0;
    private bool mainReleaseCaptured;

    private readonly List<NumberButton> runtimeButtons = new();

    // ✅ For per-block saving (accumulates only the current block's 10 trials)
    private readonly List<Dictionary<string, object>> blockDocs = new();

    // ✅ For overall statistics (independent of whether data is saved)
    private int globalTrialNo = 0;
    private int correctCount = 0;
    private long sumMs = 0;
    private float sumIPS = 0f;

    private struct HickPoint
    {
        public int N;
        public float xLog2NPlus1; // x = log2(N+1)
        public float meanRTSec;   // y = mean RT (sec)
        public float meanIPS;
    }

    private readonly List<HickPoint> hickPoints = new();

    // Accumulated values for the current block (current N)
    private float blockSumMs = 0f;
    private float blockSumIPS = 0f;
    private int blockTrialCount = 0;

    private string testId = null;

    private float ID => Mathf.Log(N + 1, 2f);

    private async void Start()
    {
        ready = false;
        if (reportLauncher == null)
            reportLauncher = FindReportLauncherInLoadedScene();

        if (resultPanel != null) resultPanel.SetActive(false);
        if (startPanelOverride != null) startPanelOverride.SetActive(false);   // Hide the reused panel during practice, show it only when practice is complete
        if (targetText != null) targetText.text = "";
        if (resultText != null) resultText.text = "";
        if (statusText != null) statusText.text = "Initializing…";
        hickPoints.Clear();

        if (spawner == null)
        {
            Debug.LogError("Spawner is not connected (connect a NumberButtonSpawnerBase in the Inspector)");
            return;
        }
        if (IsDirectSelectMode() && useSwipeOnly)
        {
            Debug.LogWarning("DirectSelect mode uses button-click input, so useSwipeOnly is turned off automatically.");
            useSwipeOnly = false;
        }
        if (useSwipeOnly && thumbSelector == null)
            Debug.LogError("Use Swipe Only is on but Thumb Selector is empty. To select via swipe, connect a Radial Swipe Selector to the ExperimentManager.");
        if (FirestoreService.I == null)
        {
            Debug.LogError("FirestoreService is missing (place it in the first scene with DontDestroyOnLoad)");
            return;
        }

        if (ExperimentConfig.I != null && ExperimentConfig.I.ShouldShowSceneIntro(out string introMsg))
            await WaitAutoSceneIntroAsync(introMsg);

        // Create Firestore session
        try
        {
            var cfg = ExperimentConfig.I;
            string activeSceneName = SceneManager.GetActiveScene().name;
            string activeSceneTitle = AutoProgressSceneTitles.TitleFor(activeSceneName);
            testId = await FirestoreService.I.CreateTestInfoAsync(
                cfg != null ? cfg.userName : "guest",
                cfg != null ? cfg.testType : "Hick",
                cfg != null ? cfg.sex : "unknown",
                cfg != null ? cfg.age : 0,
                activeSceneName,
                activeSceneTitle
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError("CreateTestInfo failed: " + e);
            // If saving is blocked but you still want to run the experiment itself, you can change this to continue instead of returning here.
            return;
        }

        // Shuffle N order (automatically capped at the maximum number of buttons the scene layout supports)
        int supportedMaxN = Mathf.Max(0, spawner.GetMaxSupportedCount());
        int effectiveMaxN = Mathf.Min(maxN, supportedMaxN);
        int effectiveMinN = Mathf.Clamp(minN, 1, Mathf.Max(1, effectiveMaxN));
        if (effectiveMaxN < 1)
        {
            Debug.LogError($"[HickExperiment] No number buttons are available in the current spawner. (supportedMax={supportedMaxN})");
            return;
        }
        if (effectiveMaxN != maxN && statusText != null)
            statusText.text = $"Due to layout limits, the maximum N is adjusted from {maxN}→{effectiveMaxN}.";

        nOrder = new List<int>();
        for (int i = effectiveMinN; i <= effectiveMaxN; i++) nOrder.Add(i);
        Shuffle(nOrder);

        blockIndex = 0;
        trialInBlock = 0;
        globalTrialNo = 0;

        ready = true;

        if (mainButton != null)
            mainButton.interactable = !IsDirectSelectMode();

        // Practice always runs at the maximum N (the most buttons) → every participant practices under the same conditions
        practiceN = effectiveMaxN;

        if (practiceTrials > 0)
            BeginPractice();      // Supports both DirectSelect and HoldMainButton
        else
            BeginMainSession();   // If practice is 0, go straight to the main session
    }

    private void ApplyCurrentN()
    {
        N = nOrder[blockIndex];

        // ✅ Create/place N buttons (the spawner calls RegisterNumberButtons to fill runtimeButtons)
        spawner.SetCount(N);

        waitingForResponse = false;
        waitingForWarmupPress = ShouldUseWarmupStart();
        warmupPressedNumbers.Clear();
        SetNumberButtonsInteractable(IsDirectSelectMode() && waitingForWarmupPress);
        if (targetText != null) targetText.text = "";

        // Reset the block save buffer
        blockDocs.Clear();

        ResetBlockSums();

        trialInBlock = 0;

        // ✅ Pre-generate this block's target order with an even distribution
        BuildBalancedTargetSequence(N, trialsPerN);
        retriedThisBlock.Clear();
    }

    /// <summary>Inserts a missed number once more into a remaining (not-yet-shown) slot of the same block.
    /// To keep the block's total trials from exceeding trialsPerN (10), one of the remaining slots is removed to make room for each insertion.
    /// If there are no remaining slots (near the end of the block), it is not re-presented.</summary>
    private void EnqueueRetryTarget(int target)
    {
        // Called in OnNumberPressed before trialInBlock is incremented → not-yet-shown slots have index > trialInBlock
        int firstFuture = trialInBlock + 1;
        if (firstFuture >= blockTargetSequence.Count) return; // No remaining slots → cannot re-present

        // Free one remaining slot so the total trials don't exceed trialsPerN (swap the position).
        if (blockTargetSequence.Count >= trialsPerN)
        {
            int dropAt = Random.Range(firstFuture, blockTargetSequence.Count);
            blockTargetSequence.RemoveAt(dropAt);
        }

        int insertAt = Random.Range(firstFuture, blockTargetSequence.Count + 1);
        blockTargetSequence.Insert(insertAt, target);
    }

    /// <summary>Builds a target order of length count so that 1~n are distributed as evenly as possible
    /// (each number appears floor/ceil times, differing by at most 1), fills blockTargetSequence, then shuffles it.</summary>
    private void BuildBalancedTargetSequence(int n, int count)
    {
        blockTargetSequence.Clear();
        if (n < 1 || count < 1) return;

        // Fill as many complete cycles (1..n) as possible
        int fullCycles = count / n;
        for (int c = 0; c < fullCycles; c++)
            for (int v = 1; v <= n; v++) blockTargetSequence.Add(v);

        // Remainder: randomly add some of 1..n one more time (at most +1)
        int remainder = count - blockTargetSequence.Count;
        if (remainder > 0)
        {
            var pool = new List<int>(n);
            for (int v = 1; v <= n; v++) pool.Add(v);
            Shuffle(pool);
            for (int i = 0; i < remainder; i++) blockTargetSequence.Add(pool[i]);
        }

        Shuffle(blockTargetSequence);
    }

    /// <summary>The target number for the current trial (trialInBlock). Falls back to plain random if there is no sequence.</summary>
    private int NextTargetForCurrentTrial()
    {
        if (trialInBlock >= 0 && trialInBlock < blockTargetSequence.Count)
            return blockTargetSequence[trialInBlock];
        return Random.Range(1, N + 1);
    }

    private void ResetBlockSums()
    {
        blockSumMs = 0f;
        blockSumIPS = 0f;
        blockTrialCount = 0;
    }

    public void MainDown()
    {
        if (!ready || IsDirectSelectMode()) return;

        isHoldingMain = true;

        if (!waitingForResponse)
        {
            if (statusText != null)
                statusText.text = inPractice
                    ? $"[Practice] {practiceDone + 1}/{practiceTrials}\nHolding…"
                    : $"N={N}  Trial {trialInBlock + 1}/{trialsPerN}\nHolding…";
            StartCoroutine(WaitAndShowTarget());
        }
    }

    public void MainUp()
    {
        if (IsDirectSelectMode()) return;
        isHoldingMain = false;

        if (!waitingForResponse)
        {
            if (statusText != null) statusText.text = "Press and hold the fingerprint button.";
        }
        else
        {
            // Tap submode: the first moment the finger leaves the fingerprint button after the stimulus is the movement onset (lift-off).
            // (For swipe, the selector passes the lift directly, so it is not captured here.)
            if (!useSwipeOnly && !mainReleaseCaptured)
            {
                mainReleaseTs = Time.realtimeSinceStartupAsDouble;
                mainReleaseCaptured = true;
            }
            if (statusText != null) statusText.text = "Lifting your finger makes the measurement unstable. Keep holding if possible.";
        }
    }

    private IEnumerator WaitAndShowTarget()
    {
        float delay = Random.Range(minDelay, maxDelay);

        float t = 0f;
        while (t < delay)
        {
            if (!isHoldingMain) yield break;
            t += Time.deltaTime;
            yield return null;
        }

        currentTarget = inPractice ? Random.Range(1, N + 1) : NextTargetForCurrentTrial();

        if (targetText != null) targetText.text = currentTarget.ToString();
        string prefix = inPractice ? "[Practice] " : "";
        if (statusText != null) statusText.text = prefix + (useSwipeOnly ? $"Now {currentTarget} — swipe in that direction from the origin" : $"Press {currentTarget} now");

        stimulusTime = Time.time;
        stimulusOnsetTs = Time.realtimeSinceStartupAsDouble;
        mainReleaseTs = -1.0;
        mainReleaseCaptured = false;
        waitingForResponse = true;
        if (!useSwipeOnly)
            SetNumberButtonsInteractable(true);
    }

    /// <param name="liftRealtime">Monotonic time (seconds) when the finger left the origin (movement onset) during a swipe. &lt;0 if none.</param>
    /// <param name="touchdownRealtime">Monotonic time (seconds) of selection confirmation (touchdown). &lt;0 if none → the call time is used.</param>
    public void OnNumberPressed(int pressedNumber, Vector2 touchScreenPos, string inputMode = null,
                                double liftRealtime = -1.0, double touchdownRealtime = -1.0)
    {
        if (HandleWarmupPress(pressedNumber)) return;
        if (!waitingForResponse) return;

        string resolvedInputMode = string.IsNullOrEmpty(inputMode)
            ? (useSwipeOnly ? "swipe" : "tap")
            : inputMode;

        // Record all three timestamps with the same monotonic clock (Time.realtimeSinceStartupAsDouble).
        double touchdownTs = touchdownRealtime >= 0.0 ? touchdownRealtime : Time.realtimeSinceStartupAsDouble;
        double liftTs = liftRealtime;   // If <0, this input (e.g. tap) has no movement onset time

        float rtSec = Time.time - stimulusTime;
        long reactionMs = (long)(rtSec * 1000f);

        bool correct = (pressedNumber == currentTarget);
        FeedbackSfx.Play(correct);   // Correct "ding" / wrong "buzz"
        float ips = ID / Mathf.Max(rtSec, 0.001f);

        // Practice trial: saved separately with is_practice=true, excluded from main statistics and block progression
        if (inPractice)
        {
            HandlePracticePress(pressedNumber, correct, reactionMs, ips, resolvedInputMode, touchScreenPos, liftTs, touchdownTs);
            return;
        }

        // Accumulate statistics
        globalTrialNo++;
        if (correct) correctCount++;
        sumMs += reactionMs;
        sumIPS += ips;

        // Block accumulation (for the regression graph)
        blockSumMs += reactionMs;
        blockSumIPS += ips;
        blockTrialCount++;

        var trialDoc = new Dictionary<string, object>
        {
            { "trial_no", globalTrialNo },
            { "block_no", blockIndex + 1 },
            { "trial_in_block", trialInBlock + 1 },
            { "n_value", N },
            { "target", currentTarget },
            { "pressed", pressedNumber },
            { "accuracy", correct },
            { "reaction_ms", reactionMs },
            { "ips", ips },
            { "input_mode", resolvedInputMode },
            { "is_practice", false },
            { "phase", "main" },
            { "created_at", Timestamp.GetCurrentTimestamp() }
        };
        AppendFittsFields(trialDoc, currentTarget, touchScreenPos);
        AppendTimingFields(trialDoc, liftTs, touchdownTs);

        // ✅ Add only to the current block buffer (collect 10 and save)
        blockDocs.Add(trialDoc);

        // ✅ If wrong, re-present that number only once within the same block (keeps 10 trials total)
        if (!correct && currentTarget >= 1 && !retriedThisBlock.Contains(currentTarget))
        {
            EnqueueRetryTarget(currentTarget);
            retriedThisBlock.Add(currentTarget);
        }

        // Prepare the next trial
        waitingForResponse = false;
        SetNumberButtonsInteractable(false);
        if (targetText != null) targetText.text = "";

        trialInBlock++;

        // End of block (10 trials) → save
        if (trialInBlock >= trialsPerN)
        {
            StartCoroutine(SaveBlockAndNext());
        }
        else
        {
            if (statusText != null)
            {
                statusText.text = IsDirectSelectMode()
                    ? $"Recorded. N={N} Trial {trialInBlock}/{trialsPerN}\nWait for the next number."
                    : $"Recorded. N={N} Trial {trialInBlock}/{trialsPerN}\nPress and hold the fingerprint button again.";
            }

            if (IsDirectSelectMode())
                StartDirectSelectLoop();
        }
    }

    private IEnumerator SaveBlockAndNext()
    {
        if (statusText != null) statusText.text = $"Saving block… (N={N})";

        var task = FirestoreService.I.WriteResultsBatchAsync(testId, blockDocs);
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted)
        {
            Debug.LogError("Block save failed: " + task.Exception);
            // Whether to continue or stop on failure is a matter of preference. For now, it continues.
        }

        // On to the next block
        RecordHickPointForCurrentBlock();
        blockIndex++;

        if (blockIndex >= nOrder.Count)
        {
            ShowFinalResult();
            yield break;
        }

        ApplyCurrentN();
        if (statusText != null)
            statusText.text = ShouldUseWarmupStart()
                ? $"Next block: N={N}. To start, press each of the {N} number buttons once."
                : IsDirectSelectMode()
                ? $"Next block: N={N}. Look at the number in the center and press the matching button."
                : $"Next block: N={N}. Press and hold the fingerprint button.";

        if (IsDirectSelectMode())
            StartDirectSelectLoop();
    }

    public void RegisterNumberButtons(List<RectTransform> spawned)
    {
        runtimeButtons.Clear();

        var ordered = new List<(int num, RectTransform rt, NumberButton nb)>(spawned.Count);
        for (int i = 0; i < spawned.Count; i++)
        {
            var rt = spawned[i];
            if (rt == null) continue;
            var nb = rt.GetComponent<NumberButton>();
            if (nb == null)
            {
                Debug.LogError($"[HickExperiment] NumberButton is missing (assign the logical number in the scene): {rt.name}");
                continue;
            }

            // Tap: in HoldMainButton mode, the fingerprint release (mainReleaseTs) is passed along as the lift-off.
            // In DirectSelect (single tap) there is no release, so lift is uncaptured (-1) → saved without decomposition.
            nb.RegisterCallback((num, pos) =>
                OnNumberPressed(num, pos, null, mainReleaseCaptured ? mainReleaseTs : -1.0));
            nb.SetInteractable(false);
            ordered.Add((nb.Number, rt, nb));
        }

        ordered.Sort((a, b) =>
        {
            int c = a.num.CompareTo(b.num);
            if (c != 0) return c;
            return string.CompareOrdinal(a.rt.name, b.rt.name);
        });

        for (int i = 0; i < ordered.Count; i++)
            runtimeButtons.Add(ordered[i].nb);

        if (thumbSelector != null)
        {
            var list = new List<(int, RectTransform)>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
                list.Add((ordered[i].num, ordered[i].rt));
            thumbSelector.SetTargets(list);
            Debug.Log($"[HickExperiment] RegisterNumberButtons: {ordered.Count} number buttons → thumbSelector.SetTargets (spokes: reuse scene/preplaced ones, or create Spoke Prefab if none)");
        }
        else if (!IsDirectSelectMode())
            Debug.LogWarning("[HickExperiment] thumbSelector is empty. Connect a Radial Swipe Selector in the ExperimentManager Inspector.");
        // In DirectSelect (touch) mode, having no swipe selector is normal, so no warning is issued
    }

    private void SetNumberButtonsInteractable(bool on)
    {
        for (int i = 0; i < runtimeButtons.Count; i++)
            runtimeButtons[i].SetInteractable(on);
    }

    void SetNumberButtonRootsActive(bool on)
    {
        for (int i = 0; i < runtimeButtons.Count; i++)
        {
            if (runtimeButtons[i] != null)
                runtimeButtons[i].gameObject.SetActive(on);
        }
    }

    private bool IsDirectSelectMode()
    {
        return startMode == StartMode.DirectSelect;
    }

    /// <summary>
    /// Stores the three raw timestamps (ms, monotonic) as-is so RT can be decomposed
    /// into decision time (stimulus→movement onset) and movement time (movement onset→touchdown).
    /// The raw values are the source of truth; the derived values (decision_ms/movement_ms) are included only for convenience.
    /// Inputs without a lift_ts (e.g. tap) store only stimulus_onset_ts/touchdown_ts.
    /// </summary>
    void AppendTimingFields(Dictionary<string, object> doc, double liftTs, double touchdownTs)
    {
        double onsetMs = stimulusOnsetTs * 1000.0;
        doc["stimulus_onset_ts"] = onsetMs;

        if (touchdownTs >= 0.0)
            doc["touchdown_ts"] = touchdownTs * 1000.0;

        if (liftTs >= 0.0)
        {
            double liftMs = liftTs * 1000.0;
            doc["lift_ts"] = liftMs;
            doc["decision_ms"] = liftMs - onsetMs;                 // Cognitive component (expected to increase logarithmically with N)
            if (touchdownTs >= 0.0)
                doc["movement_ms"] = touchdownTs * 1000.0 - liftMs; // Motor component (independent of N, expected to depend on distance)
        }
    }

    void AppendFittsFields(Dictionary<string, object> doc, int targetNumber, Vector2 touchScreenPos)
    {
        if (TryScreenToRootCanvasLocal(touchScreenPos, out Vector2 local))
        {
            doc["touch_x"] = local.x;
            doc["touch_y"] = local.y;
        }

        if (spawner != null && spawner.TryGetFittsMetrics(targetNumber, out FittsMetrics metrics))
        {
            doc["amplitude_mm"] = metrics.amplitudeMm;
            doc["target_width_mm"] = metrics.widthMm;
            doc["fitts_id_baseline"] = metrics.idBaseline;
            doc["fitts_id_ffitts"] = metrics.idFFitts;
            doc["fitts_model"] = metrics.fittsModel ?? "";
        }
    }

    bool TryScreenToRootCanvasLocal(Vector2 screenPos, out Vector2 local)
    {
        local = Vector2.zero;
        Canvas canvas = mainButton != null ? mainButton.GetComponentInParent<Canvas>() : null;
        if (canvas == null && runtimeButtons.Count > 0 && runtimeButtons[0] != null)
            canvas = runtimeButtons[0].GetComponentInParent<Canvas>();
        if (canvas == null) return false;

        RectTransform rootRt = canvas.rootCanvas.transform as RectTransform;
        if (rootRt == null) return false;

        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screenPos, cam, out local))
            return false;

        return true;
    }

    /// <summary>Mode where number-button taps are the primary input (swipe disabled). Used when RadialSwipeSelector brings the fingerprint area to the front among siblings so it doesn't cover the buttons.</summary>
    public bool UsesDirectNumberButtonInput()
    {
        return IsDirectSelectMode();
    }

    /// <summary>Exposes whether we are waiting for a response, so the swipe selector records the 'movement onset (lift-off)' time only after the stimulus is presented.</summary>
    public bool IsWaitingForResponse => waitingForResponse;

    /// <summary>
    /// The warmup (pressing every number button once) is disabled, replaced by the '3 practice trials → start button' flow.
    /// The requirePressAllButtonsToStart field is kept for compatibility with existing scenes but is no longer used.
    /// </summary>
    private bool ShouldUseWarmupStart()
    {
        return false;
    }

    private bool HandleWarmupPress(int pressedNumber)
    {
        if (!waitingForWarmupPress) return false;

        if (pressedNumber < 1 || pressedNumber > N) return true;
        warmupPressedNumbers.Add(pressedNumber);

        int remaining = Mathf.Max(0, N - warmupPressedNumbers.Count);
        if (remaining > 0)
        {
            if (statusText != null)
                statusText.text = $"Getting ready: {warmupPressedNumbers.Count}/{N} done\nPress the remaining {remaining} buttons.";
            return true;
        }

        waitingForWarmupPress = false;
        warmupCompletedForExperiment = true;
        SetNumberButtonsInteractable(false);
        if (statusText != null)
            statusText.text = $"Ready. N={N}. A number will appear at a random time.";
        StartDirectSelectLoop();
        return true;
    }

    private void StartDirectSelectLoop()
    {
        if (!ready) return;
        if (waitingForResponse) return;
        if (waitingForWarmupPress) return;
        if (blockIndex >= nOrder.Count) return;
        if (trialInBlock >= trialsPerN) return;

        if (directSelectRoutine != null)
            StopCoroutine(directSelectRoutine);
        directSelectRoutine = StartCoroutine(DirectSelectFlow());
    }

    private IEnumerator DirectSelectFlow()
    {
        float delay = Random.Range(minDelay, maxDelay);
        yield return new WaitForSeconds(delay);

        if (waitingForResponse) yield break;
        if (blockIndex >= nOrder.Count) yield break;
        if (trialInBlock >= trialsPerN) yield break;

        currentTarget = NextTargetForCurrentTrial();
        if (targetText != null) targetText.text = currentTarget.ToString();
        if (statusText != null) statusText.text = $"Press {currentTarget} now";

        stimulusTime = Time.time;
        stimulusOnsetTs = Time.realtimeSinceStartupAsDouble;
        waitingForResponse = true;
        SetNumberButtonsInteractable(true);
    }

    // ---------------- Practice (before the main session) ----------------

    /// <summary>Start practice: fixed at the maximum N; results are not saved.</summary>
    private void BeginPractice()
    {
        inPractice = true;
        practiceDone = 0;
        N = Mathf.Max(1, practiceN);

        spawner.SetCount(N);
        waitingForResponse = false;
        waitingForWarmupPress = false;
        if (targetText != null) targetText.text = "";
        SetNumberButtonsInteractable(false);
        HideStartPanel();

        if (mainButton != null)
            mainButton.interactable = !IsDirectSelectMode();

        if (statusText != null)
            statusText.text = IsDirectSelectMode()
                ? $"[Practice] 0/{practiceTrials}\nWhen a number appears, press the matching button. (Not saved)"
                : $"[Practice] 0/{practiceTrials}\nPress and hold the fingerprint button. (Not saved)";

        // DirectSelect shows targets automatically; in HoldMainButton, pressing the fingerprint button
        // brings up the practice target via the existing MainDown→WaitAndShowTarget path.
        if (IsDirectSelectMode())
            StartPracticeLoop();
    }

    private void StartPracticeLoop()
    {
        if (!ready || !inPractice) return;
        if (waitingForResponse) return;
        if (practiceRoutine != null) StopCoroutine(practiceRoutine);
        practiceRoutine = StartCoroutine(PracticeFlow());
    }

    private IEnumerator PracticeFlow()
    {
        float delay = Random.Range(minDelay, maxDelay);
        yield return new WaitForSeconds(delay);

        if (!inPractice || waitingForResponse) yield break;

        currentTarget = Random.Range(1, N + 1);
        if (targetText != null) targetText.text = currentTarget.ToString();
        if (statusText != null)
            statusText.text = $"[Practice {practiceDone + 1}/{practiceTrials}] Press {currentTarget} now";

        stimulusTime = Time.time;
        stimulusOnsetTs = Time.realtimeSinceStartupAsDouble;
        waitingForResponse = true;
        SetNumberButtonsInteractable(true);
    }

    /// <summary>Handle a practice response: saved separately as an is_practice=true document (not reflected in main statistics).</summary>
    private void HandlePracticePress(int pressedNumber, bool correct, long reactionMs, float ips,
                                     string resolvedInputMode, Vector2 touchScreenPos,
                                     double liftTs, double touchdownTs)
    {
        var doc = new Dictionary<string, object>
        {
            { "trial_no", practiceDone + 1 },
            { "block_no", 0 },                 // 0 = practice (main blocks start at 1)
            { "trial_in_block", practiceDone + 1 },
            { "n_value", N },
            { "target", currentTarget },
            { "pressed", pressedNumber },
            { "accuracy", correct },
            { "reaction_ms", reactionMs },
            { "ips", ips },
            { "input_mode", resolvedInputMode },
            { "is_practice", true },
            { "phase", "practice" },
            { "created_at", Timestamp.GetCurrentTimestamp() }
        };
        AppendFittsFields(doc, currentTarget, touchScreenPos);
        AppendTimingFields(doc, liftTs, touchdownTs);
        practiceDocs.Add(doc);

        waitingForResponse = false;
        SetNumberButtonsInteractable(false);
        if (targetText != null) targetText.text = "";

        practiceDone++;
        if (practiceDone >= Mathf.Max(1, practiceTrials))
        {
            inPractice = false;
            if (practiceRoutine != null) { StopCoroutine(practiceRoutine); practiceRoutine = null; }
            StartCoroutine(SavePracticeDocs());
            ShowStartPanel();
            return;
        }

        if (statusText != null)
            statusText.text = IsDirectSelectMode()
                ? $"[Practice] {practiceDone}/{practiceTrials} done"
                : $"[Practice] {practiceDone}/{practiceTrials} done\nPress and hold the fingerprint button again.";
        if (IsDirectSelectMode())
            StartPracticeLoop();
        // HoldMainButton: when the user presses the fingerprint button again, the next practice target appears via WaitAndShowTarget
    }

    /// <summary>Save the practice trials to Firestore as is_practice=true documents.</summary>
    private IEnumerator SavePracticeDocs()
    {
        if (string.IsNullOrEmpty(testId) || practiceDocs.Count == 0) yield break;

        var batch = new List<Dictionary<string, object>>(practiceDocs);
        practiceDocs.Clear();

        var task = FirestoreService.I.WriteResultsBatchAsync(testId, batch);
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted)
            Debug.LogError("[HickExperiment] Failed to save practice data: " + task.Exception);
        else
            Debug.Log($"[HickExperiment] Saved {batch.Count} practice records (is_practice=true)");
    }

    /// <summary>Start the main session when the start button is pressed (or when there is no practice).</summary>
    private void BeginMainSession()
    {
        inPractice = false;
        HideStartPanel();

        blockIndex = 0;
        trialInBlock = 0;
        globalTrialNo = 0;

        ApplyCurrentN();

        if (statusText != null)
            statusText.text = IsDirectSelectMode()
                ? $"Main session started. N={N}. Look at the number in the center and press the matching button."
                : $"Main session started. N={N}. Press and hold the fingerprint button.";

        if (IsDirectSelectMode())
            StartDirectSelectLoop();
    }

    private RectTransform GetUiCanvasRoot()
    {
        Canvas canvas = mainButton != null ? mainButton.GetComponentInParent<Canvas>() : null;
        if (canvas == null && runtimeButtons.Count > 0 && runtimeButtons[0] != null)
            canvas = runtimeButtons[0].GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        return canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
    }

    private void EnsureStartPanel()
    {
        if (startPanel != null) return;

        // If the existing scene's 'Start' panel is connected, reuse it instead of creating a new one
        if (startPanelOverride != null)
        {
            startPanel = startPanelOverride;
            startPanelMessage = startPanelMessageOverride;
            WireStartButton(startPanel);
            startPanel.SetActive(false);
            return;
        }

        RectTransform root = GetUiCanvasRoot();
        if (root == null)
        {
            Debug.LogError("[HickExperiment] Could not find a Canvas, so the start panel cannot be created.");
            return;
        }

        startPanel = new GameObject("PracticeDonePanel", typeof(RectTransform), typeof(Image));
        startPanel.transform.SetParent(root, false);
        var panelRt = startPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = startPanel.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.78f);
        panelImg.raycastTarget = true;   // Block input to the number buttons behind it

        var msgGo = new GameObject("Message", typeof(RectTransform), typeof(Text));
        msgGo.transform.SetParent(panelRt, false);
        var msgRt = msgGo.GetComponent<RectTransform>();
        msgRt.anchorMin = new Vector2(0.08f, 0.45f);
        msgRt.anchorMax = new Vector2(0.92f, 0.75f);
        msgRt.offsetMin = Vector2.zero;
        msgRt.offsetMax = Vector2.zero;
        startPanelMessage = msgGo.GetComponent<Text>();
        startPanelMessage.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        startPanelMessage.fontSize = 40;
        startPanelMessage.alignment = TextAnchor.MiddleCenter;
        startPanelMessage.color = Color.white;
        startPanelMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
        startPanelMessage.verticalOverflow = VerticalWrapMode.Overflow;
        startPanelMessage.raycastTarget = false;

        var btnGo = new GameObject("StartButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(panelRt, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.28f, 0.22f);
        btnRt.anchorMax = new Vector2(0.72f, 0.36f);
        btnRt.offsetMin = Vector2.zero;
        btnRt.offsetMax = Vector2.zero;
        btnGo.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.9f, 1f);
        btnGo.GetComponent<Button>().onClick.AddListener(BeginMainSession);

        var lblGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        lblGo.transform.SetParent(btnRt, false);
        var lblRt = lblGo.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;
        var lbl = lblGo.GetComponent<Text>();
        lbl.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lbl.fontSize = 48;
        lbl.alignment = TextAnchor.MiddleCenter;
        lbl.color = Color.white;
        lbl.text = "start";
        lbl.raycastTarget = false;

        startPanel.SetActive(false);
    }

    /// <summary>Find the start button of the reused panel and wire its onClick to BeginMainSession (avoiding duplicates).</summary>
    private void WireStartButton(GameObject panel)
    {
        Button btn = startButtonOverride;
        if (btn == null && panel != null)
            btn = panel.GetComponentInChildren<Button>(true);
        if (btn == null)
        {
            Debug.LogWarning("[HickExperiment] Could not find a start button (Button) in the Start panel. Connect a Start Button Override.");
            return;
        }
        btn.onClick.RemoveListener(BeginMainSession);
        btn.onClick.AddListener(BeginMainSession);
    }

    private void ShowStartPanel()
    {
        string readyMsg = $"{practiceTrials} practice trials complete\n\nWhen you're ready, press the button below\nto start the main session.";

        // 1) If the intro panel used at scene start (AutoProgressSceneIntroUI) exists, reuse it — no separate connection needed, video is skipped
        var introUi = FindBehaviourInLoadedScene<AutoProgressSceneIntroUI>();
        if (introUi != null)
        {
            if (statusText != null) statusText.text = "Practice complete";
            _ = ShowIntroThenBeginMainSessionAsync(introUi, readyMsg);
            return;
        }

        // 2) If there is no intro panel, use the legacy approach (reuse a connected panel or create one at runtime)
        EnsureStartPanel();
        if (startPanel == null) { BeginMainSession(); return; }

        if (startPanelMessage != null)
            startPanelMessage.text = readyMsg;
        if (statusText != null) statusText.text = "Practice complete";

        startPanel.SetActive(true);
        startPanel.transform.SetAsLastSibling();
    }

    /// <summary>After practice, show the intro panel (with the start button), and start the main session when the button is pressed.</summary>
    private async Task ShowIntroThenBeginMainSessionAsync(AutoProgressSceneIntroUI introUi, string message)
    {
        try
        {
            await introUi.WaitUntilProceedImmediateAsync(message);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[HickExperiment] Error while reusing the intro panel: " + e);
        }
        BeginMainSession();
    }

    private void HideStartPanel()
    {
        if (startPanel != null) startPanel.SetActive(false);
    }

    private void ShowFinalResult()
    {
        ClearGraphLabels();

        bool autoChain = ExperimentConfig.I != null &&
                         ExperimentConfig.I.autoProgressKind != ExperimentConfig.AutoProgressKind.None;
        bool hasNextAuto = autoChain && ExperimentConfig.I.PeekHasNextAutoScene();

        if (resultPanel != null) resultPanel.SetActive(false);
        if (resultText != null) resultText.text = "";
        if (targetText != null) targetText.text = "";

        if (statusText != null)
        {
            statusText.text = hasNextAuto
                ? ""
                : "You can view the results on the web.";
        }

        if (reportLauncher != null)
            reportLauncher.PublishTestAndOpenReport(testId);
        else
            Debug.LogWarning("[HickExperiment] Could not find a ResultReportLauncher, so skipping auto-opening the results web page.");

        if (hasNextAuto)
        {
            // After finishing, load the next scene immediately without showing an intermediate prompt.
            // The guidance ("this is ~") → explanatory video → start button flow is handled at the start of the next scene
            // by AutoProgressSceneIntroUI (via WaitAutoSceneIntroAsync).
            SetNumberButtonRootsActive(false);
            if (ExperimentConfig.I != null && ExperimentConfig.I.TryAdvanceAutoProgressChain(out string next))
                SceneManager.LoadScene(next);
            else
                SceneManager.LoadScene("fin");
            return;
        }

        if (ExperimentConfig.I != null)
            ExperimentConfig.I.ClearAutoProgress();

        // In auto-chain mode, once all scenes are finished, move to the end (fin) scene.
        if (autoChain)
            SceneManager.LoadScene("fin");
    }

    static async Task WaitAutoSceneIntroAsync(string message)
    {
        var introUi = FindBehaviourInLoadedScene<AutoProgressSceneIntroUI>();
        if (introUi != null)
            await introUi.WaitUntilProceedAsync(message);
        else
            await AutoProgressIntroOverlay.WaitUntilDismissed(message);
    }

    static T FindBehaviourInLoadedScene<T>() where T : MonoBehaviour
    {
        var all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (!c.gameObject.scene.IsValid() || !c.gameObject.scene.isLoaded) continue;
            return c;
        }
        return null;
    }

    private ResultReportLauncher FindReportLauncherInLoadedScene()
    {
        // Search including inactive objects (for scenes where the result panel starts inactive)
        var all = Resources.FindObjectsOfTypeAll<ResultReportLauncher>();
        for (int i = 0; i < all.Length; i++)
        {
            var launcher = all[i];
            if (launcher == null) continue;
            if (!launcher.gameObject.scene.IsValid()) continue;
            if (!launcher.gameObject.scene.isLoaded) continue;
            return launcher;
        }
        return null;
    }

    private string TrySaveCurrentGraphPng()
    {
        try
        {
            if (hickGraphRawImage == null) return "";
            Texture2D tex = hickGraphRawImage.texture as Texture2D;
            if (tex == null) return "";

            byte[] pngBytes = tex.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0) return "";

            string safeTestId = string.IsNullOrWhiteSpace(testId) ? "unknown" : SanitizeFileName(testId);
            string safePrefix = string.IsNullOrWhiteSpace(graphImageFilePrefix) ? "hick_graph" : SanitizeFileName(graphImageFilePrefix);
            string fileName = $"{safePrefix}_{safeTestId}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllBytes(fullPath, pngBytes);
            Debug.Log($"[HickExperiment] Graph PNG saved: {fullPath}");
            return fullPath;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[HickExperiment] Graph PNG save failed: " + e.Message);
            return "";
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            input = input.Replace(invalid[i], '_');
        return input;
    }

    private void RecordHickPointForCurrentBlock()
    {
        if (blockTrialCount <= 0) return;

        float meanRT = (blockSumMs / Mathf.Max(1, blockTrialCount)) / 1000f;
        float meanIPS = blockSumIPS / Mathf.Max(1, blockTrialCount);
        float x = Mathf.Log(N + 1, 2f);

        hickPoints.Add(new HickPoint
        {
            N = N,
            xLog2NPlus1 = x,
            meanRTSec = meanRT,
            meanIPS = meanIPS
        });
    }

    private static bool TryComputeLinearRegression(List<HickPoint> points, out float a, out float b, out float r2)
    {
        a = 0f;
        b = 0f;
        r2 = 0f;

        if (points == null || points.Count < 2) return false;

        float meanX = 0f;
        float meanY = 0f;
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            meanX += points[i].xLog2NPlus1;
            meanY += points[i].meanRTSec;
        }
        meanX /= n;
        meanY /= n;

        float sxx = 0f;
        float sxy = 0f;
        for (int i = 0; i < n; i++)
        {
            float dx = points[i].xLog2NPlus1 - meanX;
            float dy = points[i].meanRTSec - meanY;
            sxx += dx * dx;
            sxy += dx * dy;
        }

        if (Mathf.Abs(sxx) < 1e-9f) return false;

        b = sxy / sxx;
        a = meanY - b * meanX;

        float ssTot = 0f;
        float ssRes = 0f;
        for (int i = 0; i < n; i++)
        {
            float x = points[i].xLog2NPlus1;
            float y = points[i].meanRTSec;
            float yHat = a + b * x;
            float diff = y - yHat;
            ssRes += diff * diff;

            float tot = y - meanY;
            ssTot += tot * tot;
        }

        if (Mathf.Abs(ssTot) < 1e-9f) r2 = 0f;
        else r2 = 1f - (ssRes / ssTot);

        return true;
    }

    private void ClearGraphLabels()
    {
        if (graphTitleText != null) graphTitleText.text = "";
        if (graphXAxisLabel != null) graphXAxisLabel.text = "";
        if (graphYAxisLabel != null) graphYAxisLabel.text = "";
    }

    private void DrawHickGraph(List<HickPoint> points, float a, float b, float r2)
    {
        if (hickGraphRawImage == null) return;
        if (points == null || points.Count < 2) return;

        int w = Mathf.Max(320, graphWidth);
        int h = Mathf.Max(200, graphHeight);

        float xMin = float.MaxValue;
        float xMax = float.MinValue;
        float yMin = float.MaxValue;
        float yMax = float.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            xMin = Mathf.Min(xMin, points[i].xLog2NPlus1);
            xMax = Mathf.Max(xMax, points[i].xLog2NPlus1);
            yMin = Mathf.Min(yMin, points[i].meanRTSec);
            yMax = Mathf.Max(yMax, points[i].meanRTSec);
        }

        float xRange = Mathf.Max(1e-6f, xMax - xMin);
        float yRange = Mathf.Max(1e-6f, yMax - yMin);
        yMin -= yRange * 0.1f;
        yMax += yRange * 0.1f;
        yRange = yMax - yMin;

        // UI Text axis labels (drawing non-ASCII on the texture is hard, so a separate Text is recommended)
        if (graphTitleText != null)
            graphTitleText.text = $"RT = {a:0.###} + {b:0.###} * log2(N+1), R^2 = {r2:0.###}";
        if (graphXAxisLabel != null)
            graphXAxisLabel.text = "log2(N+1)";
        if (graphYAxisLabel != null)
            graphYAxisLabel.text = "Mean RT (s)";

        // Create background (Texture2D: pixel y=0 is at the bottom of the screen)
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[w * h];
        Color32 bg = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        int padL = 56;
        int padR = 20;
        int padT = 24;
        int padB = 44;
        int plotW = w - padL - padR;
        int plotBottomPy = padB;
        int plotTopPy = h - padT;
        int plotH = Mathf.Max(1, plotTopPy - plotBottomPy);

        int ToPx(float x)
        {
            return padL + Mathf.RoundToInt(((x - xMin) / xRange) * plotW);
        }

        // Larger data y is higher on screen (larger pixel y)
        int ToPy(float y)
        {
            float t = (y - yMin) / yRange;
            return plotBottomPy + Mathf.RoundToInt(t * plotH);
        }

        void SetPixel(int px, int py, Color32 c)
        {
            if (px < 0 || px >= w || py < 0 || py >= h) return;
            pixels[py * w + px] = c;
        }

        void DrawLine(int x0, int y0, int x1, int y1, Color32 c)
        {
            // Bresenham
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                SetPixel(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        void DrawCircleFilled(int cx, int cy, int radius, Color32 c)
        {
            int r2 = radius * radius;
            for (int yy = -radius; yy <= radius; yy++)
            {
                for (int xx = -radius; xx <= radius; xx++)
                {
                    if (xx * xx + yy * yy > r2) continue;
                    SetPixel(cx + xx, cy + yy, c);
                }
            }
        }

        Color32 axisColor = new Color32(0, 0, 0, 255);
        Color32 gridColor = new Color32(220, 220, 220, 255);
        Color32 pointColor = new Color32(0, 95, 200, 255);
        Color32 lineColor = new Color32(220, 0, 0, 255);

        // Axes: the x-axis is at the bottom of the plot (plotBottomPy), the y-axis on the left (padL)
        int yAxisX = padL;
        DrawLine(yAxisX, plotBottomPy, yAxisX, plotTopPy, axisColor);
        DrawLine(padL, plotBottomPy, padL + plotW, plotBottomPy, axisColor);

        for (int gx = 1; gx <= 3; gx++)
        {
            int x = padL + (int)((plotW * (float)gx) / 4f);
            DrawLine(x, plotBottomPy, x, plotTopPy, gridColor);
        }
        for (int gy = 1; gy <= 3; gy++)
        {
            int py = plotBottomPy + (int)((plotH * (float)gy) / 4f);
            DrawLine(padL, py, padL + plotW, py, gridColor);
        }

        // Points
        for (int i = 0; i < points.Count; i++)
        {
            int px = ToPx(points[i].xLog2NPlus1);
            int py = ToPy(points[i].meanRTSec);
            DrawCircleFilled(px, py, 5, pointColor);
        }

        // Regression line
        float yAtXMin = a + b * xMin;
        float yAtXMax = a + b * xMax;
        int rx0 = ToPx(xMin);
        int ry0 = ToPy(yAtXMin);
        int rx1 = ToPx(xMax);
        int ry1 = ToPy(yAtXMax);
        DrawLine(rx0, ry0, rx1, ry1, lineColor);

        tex.SetPixels32(pixels);
        tex.Apply();

        if (hickGraphRawImage.texture != null)
        {
            // Clean up the previous texture at runtime (assuming Unity is not referencing an editor asset)
            Destroy(hickGraphRawImage.texture);
        }
        hickGraphRawImage.texture = tex;
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}