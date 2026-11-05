using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FittsCalibrationExperiment : MonoBehaviour
{
    enum SessionPhase
    {
        PracticeA_Start,
        PracticeA_Target,
        WaitingProceedAfterA,
        PracticeB_Start,
        PracticeB_Target,
        WaitingProceedAfterB,
        WaitingProceedAfterCondition,
        Data_Start,
        Data_Target,
        Finished,
    }

    struct TrialCondition
    {
        public float amplitudeMm;
        public float widthMm;
        public int conditionIndex;
    }

    [Header("Layout")]
    [Tooltip("Top guidance (StatusText) anchor. The lower the Y, the further down the text moves.")]
    [SerializeField] private Vector2 statusAnchorMin = new Vector2(0.05f, 0.72f);
    [SerializeField] private Vector2 statusAnchorMax = new Vector2(0.95f, 0.88f);

    [Header("UI (created at runtime if left empty)")]
    [SerializeField] private RectTransform uiRoot;
    [SerializeField] private RectTransform startButtonRect;
    [SerializeField] private RectTransform targetButtonRect;
    [SerializeField] private Text statusText;
    [SerializeField] private Text resultText;
    [SerializeField] private GameObject resultPanel;

    [Header("Geometry")]
    [SerializeField] private Vector2 fittsReferenceResolution = new Vector2(1080f, 2340f);
    [Tooltip("Device physical pixel density (px/mm). If 0, auto-calculated from the physical diagonal/resolution below. If it doesn't match when measured with a ruler, enter the actual value here directly.")]
    [SerializeField] private float pixelsPerMm = 0f;
    [Tooltip("Device physical screen diagonal size (mm). Used to calculate px/mm from the actual render resolution on Android devices where Screen.dpi is inaccurate. Stays accurate regardless of resolution setting (FHD+/QHD+). Galaxy S24 Ultra = 6.8 inch ≈ 172.72mm")]
    [SerializeField] private float physicalDiagonalMm = 172.72f;
    [Tooltip("Fallback DPI used when physicalDiagonalMm=0 and Screen.dpi is also invalid")]
    [SerializeField] private float fallbackDpi = 160f;
    [Tooltip("Target direction from the start button center (degrees). -90 = down. Used only when random placement is off")]
    [SerializeField] private float movementAngleDeg = -90f;
    [SerializeField] private Vector2 startAnchoredPosition = new Vector2(0f, 600f);
    [Tooltip("Start (green) button diameter (mm). Based on the paper's 6mm start bar width")]
    [SerializeField] private float startButtonSizeMm = 6f;

    [Header("Placement (paper-style random)")]
    [Tooltip("When on, keeps distance A fixed but places the start/target circles at a random position/direction each trial")]
    [SerializeField] private bool randomizePlacement = true;
    [Tooltip("Fraction of the screen top to avoid placement in (protects the status text area)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float topReserveFraction = 0.18f;
    [Tooltip("Screen edge margin (mm)")]
    [SerializeField] private float edgePaddingMm = 2f;

    [Header("Conditions (mm)")]
    [Tooltip("A = 20/30/45/60 mm per Yamanaka & Usuba 2021")]
    [SerializeField] private float[] amplitudesMm = { 20f, 30f, 45f, 60f };
    [Tooltip("W = 2/4/6/8/10 mm per Yamanaka & Usuba 2021")]
    [SerializeField] private float[] widthsMm = { 2f, 4f, 6f, 8f, 10f };
    [Tooltip("16 repetitions per condition per the paper")]
    [SerializeField] private int trialsPerCondition = 16;

    [Header("Practice")]
    [Tooltip("A (mm) used for Practice A, the task-familiarization practice at the very start of the session")]
    [SerializeField] private float practiceAAmplitudeMm = 30f;
    [Tooltip("W (mm) used for Practice A")]
    [SerializeField] private float practiceAWidthMm = 6f;
    [Tooltip("Practice B: number of successful practice trials required per condition")]
    [SerializeField] private int practiceBTrialsRequired = 1;

    [Header("Timing")]
    [SerializeField] private float interTrialDelaySec = 0.4f;

    [Header("Save")]
    [SerializeField] private int saveBatchSize = 24;

    [Header("Finish")]
    [Tooltip("Name of the scene the 'To Main' button navigates to after completion")]
    [SerializeField] private string mainSceneName = "main";

    [Header("Audio")]
    [Tooltip("Played when tapping the start bar. If empty, uses an auto-generated click sound")]
    [SerializeField] private AudioClip startClickClip;
    [Tooltip("Played on target success. If empty, uses an auto-generated bell sound")]
    [SerializeField] private AudioClip targetSuccessClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 0.7f;

    readonly List<TrialCondition> conditionBlocks = new();
    readonly List<Dictionary<string, object>> pendingDocs = new();

    RectTransform rootRt;
    Image startImage;
    Image targetImage;
    RectTransform targetVisualRect;
    FittsCalibrationTapTarget startTap;
    FittsCalibrationTapTarget targetTap;
    RectTransform fullscreenCatchRect;
    FittsCalibrationTapTarget catchTap;
    GameObject proceedPanel;
    Text proceedMessageText;
    Button proceedButton;

    string testId;
    SessionPhase phase = SessionPhase.Finished;
    int conditionBlockIndex;
    int trialInCondition;
    int practiceBSuccessCount;
    int globalTrialNo;
    TrialCondition current;
    Vector2 currentStartPos;
    float currentAngleDeg;
    float stimulusTime;
    Coroutine flowRoutine;
    System.Action proceedCallback;

    AudioSource sfxSource;

    static Sprite sharedCircleSprite;

    async void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
        if (resultText != null) resultText.text = "";
        SetStatus("Initializing…");

        EnsureUi();
        if (rootRt == null)
        {
            Debug.LogError("[FittsCalibration] Failed to create the UI root.");
            return;
        }

        LogPhysicalSizeDiagnostics();

        if (FirestoreService.I == null)
        {
            Debug.LogError("[FittsCalibration] FirestoreService is missing. Start from the main scene or add a FirestoreService to the scene.");
            return;
        }

        if (ExperimentConfig.I != null && ExperimentConfig.I.ShouldShowSceneIntro(out string introMsg))
            await WaitIntroAsync(introMsg);

        try
        {
            var cfg = ExperimentConfig.I;
            string sceneName = SceneManager.GetActiveScene().name;
            testId = await FirestoreService.I.CreateTestInfoAsync(
                cfg != null ? cfg.userName : "guest",
                cfg != null ? cfg.testType : "FittsCCalibration",
                cfg != null ? cfg.sex : "unknown",
                cfg != null ? cfg.age : 0,
                sceneName,
                AutoProgressSceneTitles.TitleFor(sceneName));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FittsCalibration] CreateTestInfo failed: " + e);
            return;
        }

        BuildConditionBlocks();
        conditionBlockIndex = 0;
        trialInCondition = 0;
        globalTrialNo = 0;

        BeginPracticeA();
    }

    static async Task WaitIntroAsync(string message)
    {
        var introUi = Object.FindFirstObjectByType<AutoProgressSceneIntroUI>();
        if (introUi != null)
            await introUi.WaitUntilProceedAsync(message);
        else
            await AutoProgressIntroOverlay.WaitUntilDismissed(message);
    }

    void EnsureUi()
    {
        EnsureEventSystem();
        EnsureAudio();

        if (uiRoot == null)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                if (Object.FindFirstObjectByType<EventSystem>() == null)
                    EnsureEventSystem();

                var canvasGo = new GameObject("FittsCalibCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = fittsReferenceResolution;
                scaler.matchWidthOrHeight = 0.5f;
            }

            uiRoot = canvas.transform as RectTransform;
        }

        rootRt = uiRoot != null ? uiRoot.GetComponentInParent<Canvas>()?.rootCanvas?.transform as RectTransform : null;
        if (rootRt == null && uiRoot != null)
            rootRt = uiRoot;

        if (statusText == null)
            statusText = CreateStatusText(uiRoot, statusAnchorMin, statusAnchorMax);

        if (startButtonRect == null)
            startButtonRect = CreateTapButton(uiRoot, "StartButton", new Color(0.2f, 0.75f, 0.35f, 1f), out startImage, out startTap, FittsCalibrationTapTarget.TapRole.Start);

        if (targetButtonRect == null)
            targetButtonRect = CreateTargetHitArea(uiRoot, out targetTap);

        if (targetVisualRect == null)
        {
            targetVisualRect = CreateTargetVisual(targetButtonRect, out targetImage);
        }

        if (fullscreenCatchRect == null)
            fullscreenCatchRect = CreateFullscreenCatcher(uiRoot, out catchTap);

        ApplyStartButtonSize();
        startButtonRect.anchoredPosition = startAnchoredPosition;

        EnsureProceedPanel();
        EnsureResultPanel();
    }

    static void EnsureEventSystem()
    {
        var es = Object.FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es = go.GetComponent<EventSystem>();
        }

        var module = es.GetComponent<InputSystemUIInputModule>();
        if (module == null)
            module = es.gameObject.AddComponent<InputSystemUIInputModule>();

        if (module.actionsAsset == null)
            module.AssignDefaultActions();
    }

    void EnsureProceedPanel()
    {
        if (proceedPanel != null) return;

        proceedPanel = new GameObject("ProceedPanel", typeof(RectTransform), typeof(Image));
        proceedPanel.transform.SetParent(uiRoot, false);
        var panelRt = proceedPanel.GetComponent<RectTransform>();
        StretchFull(panelRt);
        var panelImg = proceedPanel.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.72f);
        panelImg.raycastTarget = true;
        proceedPanel.SetActive(false);

        var msgGo = new GameObject("ProceedMessage", typeof(RectTransform), typeof(Text));
        msgGo.transform.SetParent(panelRt, false);
        var msgRt = msgGo.GetComponent<RectTransform>();
        msgRt.anchorMin = new Vector2(0.08f, 0.42f);
        msgRt.anchorMax = new Vector2(0.92f, 0.72f);
        msgRt.offsetMin = Vector2.zero;
        msgRt.offsetMax = Vector2.zero;
        proceedMessageText = msgGo.GetComponent<Text>();
        proceedMessageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        proceedMessageText.fontSize = 34;
        proceedMessageText.alignment = TextAnchor.MiddleCenter;
        proceedMessageText.color = Color.white;
        proceedMessageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        proceedMessageText.verticalOverflow = VerticalWrapMode.Overflow;
        proceedMessageText.raycastTarget = false;

        var btnGo = new GameObject("ProceedButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(panelRt, false);
        var btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.25f, 0.18f);
        btnRt.anchorMax = new Vector2(0.75f, 0.32f);
        btnRt.offsetMin = Vector2.zero;
        btnRt.offsetMax = Vector2.zero;
        var btnImg = btnGo.GetComponent<Image>();
        btnImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        proceedButton = btnGo.GetComponent<Button>();
        proceedButton.onClick.AddListener(OnProceedClicked);

        var btnLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        btnLabelGo.transform.SetParent(btnRt, false);
        var labelRt = btnLabelGo.GetComponent<RectTransform>();
        StretchFull(labelRt);
        var label = btnLabelGo.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 40;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "Continue";
        label.raycastTarget = false;
    }

    void EnsureResultPanel()
    {
        if (resultPanel != null) return;

        resultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
        resultPanel.transform.SetParent(uiRoot, false);
        var panelRt = resultPanel.GetComponent<RectTransform>();
        StretchFull(panelRt);
        var panelImg = resultPanel.GetComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.82f);
        panelImg.raycastTarget = true;
        resultPanel.SetActive(false);

        var textGo = new GameObject("ResultText", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(panelRt, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.05f, 0.28f);
        textRt.anchorMax = new Vector2(0.95f, 0.95f);
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        resultText = textGo.GetComponent<Text>();
        resultText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        resultText.fontSize = 36;
        resultText.alignment = TextAnchor.MiddleCenter;
        resultText.color = Color.white;
        resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
        resultText.verticalOverflow = VerticalWrapMode.Overflow;
        resultText.raycastTarget = false;

        var mainBtnGo = new GameObject("MainButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        mainBtnGo.transform.SetParent(panelRt, false);
        var mainBtnRt = mainBtnGo.GetComponent<RectTransform>();
        mainBtnRt.anchorMin = new Vector2(0.25f, 0.1f);
        mainBtnRt.anchorMax = new Vector2(0.75f, 0.2f);
        mainBtnRt.offsetMin = Vector2.zero;
        mainBtnRt.offsetMax = Vector2.zero;
        var mainBtnImg = mainBtnGo.GetComponent<Image>();
        mainBtnImg.color = new Color(0.2f, 0.45f, 0.85f, 1f);
        var mainBtn = mainBtnGo.GetComponent<Button>();
        mainBtn.onClick.AddListener(GoToMain);

        var mainLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        mainLabelGo.transform.SetParent(mainBtnRt, false);
        var mainLabelRt = mainLabelGo.GetComponent<RectTransform>();
        StretchFull(mainLabelRt);
        var mainLabel = mainLabelGo.GetComponent<Text>();
        mainLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mainLabel.fontSize = 40;
        mainLabel.alignment = TextAnchor.MiddleCenter;
        mainLabel.color = Color.white;
        mainLabel.text = "To Main";
        mainLabel.raycastTarget = false;
    }

    void GoToMain()
    {
        SceneManager.LoadScene(mainSceneName);
    }

    void EnsureAudio()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }

        if (startClickClip == null)
            startClickClip = GenerateClickClip();
        if (targetSuccessClip == null)
            targetSuccessClip = GenerateBellClip();
    }

    void PlayStartClick()
    {
        if (sfxSource != null && startClickClip != null)
            sfxSource.PlayOneShot(startClickClip, sfxVolume);
    }

    void PlayTargetSuccess()
    {
        if (sfxSource != null && targetSuccessClip != null)
            sfxSource.PlayOneShot(targetSuccessClip, sfxVolume);
    }

    static AudioClip GenerateClickClip()
    {
        const int sampleRate = 44100;
        int samples = sampleRate / 20;
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 60f);
            data[i] = Mathf.Sin(2f * Mathf.PI * 1200f * t) * env;
        }

        var clip = AudioClip.Create("FittsClick", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip GenerateBellClip()
    {
        const int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.5f);
        var data = new float[samples];
        float[] freqs = { 880f, 1318.5f };
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 6f);
            float s = 0f;
            for (int f = 0; f < freqs.Length; f++)
                s += Mathf.Sin(2f * Mathf.PI * freqs[f] * t);
            data[i] = s / freqs.Length * env;
        }

        var clip = AudioClip.Create("FittsBell", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static Text CreateStatusText(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject("StatusText", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 32;
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static Sprite GetCircleSprite()
    {
        if (sharedCircleSprite != null) return sharedCircleSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        float radius = size * 0.5f - 0.5f;
        var center = new Vector2(radius, radius);
        var pixels = new Color32[size * size];
        float radiusSq = radius * radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                pixels[y * size + x] = dx * dx + dy * dy <= radiusSq
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        sharedCircleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return sharedCircleSprite;
    }

    static void SetupCircleImage(Image image, Color color, bool raycastTarget)
    {
        image.sprite = GetCircleSprite();
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = color;
        image.raycastTarget = raycastTarget;
    }

    RectTransform CreateTapButton(RectTransform parent, string name, Color color, out Image image, out FittsCalibrationTapTarget tap, FittsCalibrationTapTarget.TapRole role)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FittsCalibrationTapTarget));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        image = go.GetComponent<Image>();
        SetupCircleImage(image, color, raycastTarget: true);
        tap = go.GetComponent<FittsCalibrationTapTarget>();
        tap.Configure(this, role);
        rt.SetAsLastSibling();
        return rt;
    }

    RectTransform CreateTargetHitArea(RectTransform parent, out FittsCalibrationTapTarget tap)
    {
        var go = new GameObject("TargetHitArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FittsCalibrationTapTarget));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var hitImage = go.GetComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;
        tap = go.GetComponent<FittsCalibrationTapTarget>();
        tap.Configure(this, FittsCalibrationTapTarget.TapRole.Target);
        rt.SetAsLastSibling();
        return rt;
    }

    RectTransform CreateFullscreenCatcher(RectTransform parent, out FittsCalibrationTapTarget tap)
    {
        var go = new GameObject("FullscreenTapCatcher", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(FittsCalibrationTapTarget));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        StretchFull(rt);
        var img = go.GetComponent<Image>();
        img.color = Color.clear;      // transparent: the target below stays visible
        img.raycastTarget = true;     // receives taps anywhere on the screen
        tap = go.GetComponent<FittsCalibrationTapTarget>();
        tap.Configure(this, FittsCalibrationTapTarget.TapRole.Target);
        go.SetActive(false);
        return rt;
    }

    /// <summary>Enables the fullscreen catcher only during the measurement/target phase. When on, a tap anywhere
    /// on the screen is judged and recorded as hit/miss relative to the target center (including taps that miss the small target by a wide margin).</summary>
    void SetFullscreenCatch(bool active)
    {
        if (fullscreenCatchRect == null) return;
        if (active)
        {
            fullscreenCatchRect.gameObject.SetActive(true);
            fullscreenCatchRect.SetAsLastSibling(); // raise above the target to intercept all taps
        }
        else
        {
            fullscreenCatchRect.gameObject.SetActive(false);
        }
    }

    static RectTransform CreateTargetVisual(RectTransform hitParent, out Image image)
    {
        var go = new GameObject("TargetVisual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(hitParent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        image = go.GetComponent<Image>();
        SetupCircleImage(image, new Color(0.25f, 0.45f, 0.95f, 1f), raycastTarget: false);
        return rt;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void BuildConditionBlocks()
    {
        conditionBlocks.Clear();
        int conditionIndex = 0;
        for (int ai = 0; ai < amplitudesMm.Length; ai++)
        {
            for (int wi = 0; wi < widthsMm.Length; wi++)
            {
                float a = amplitudesMm[ai];
                float w = widthsMm[wi];
                if (a <= 0f || w <= 0f) continue;
                conditionBlocks.Add(new TrialCondition
                {
                    amplitudeMm = a,
                    widthMm = w,
                    conditionIndex = conditionIndex,
                });
                conditionIndex++;
            }
        }

        Shuffle(conditionBlocks);
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void BringTapButtonsToFront()
    {
        if (targetButtonRect != null && targetButtonRect.gameObject.activeSelf)
            targetButtonRect.SetAsLastSibling();
        if (startButtonRect != null && startButtonRect.gameObject.activeSelf)
            startButtonRect.SetAsLastSibling();
    }

    void BeginPracticeA()
    {
        current = new TrialCondition
        {
            amplitudeMm = practiceAAmplitudeMm,
            widthMm = practiceAWidthMm,
            conditionIndex = -1,
        };
        phase = SessionPhase.PracticeA_Start;
        HideProceedPanel();
        PrepareTrialPlacement(current);
        ApplyTargetLayout(current, visible: false);
        startButtonRect.gameObject.SetActive(true);
        BringTapButtonsToFront();
        SetStatus("[Practice A] Learn the task flow\nTap in order: green start → blue target.");
    }

    void BeginPracticeB()
    {
        if (conditionBlockIndex >= conditionBlocks.Count)
        {
            FinishExperiment();
            return;
        }

        current = conditionBlocks[conditionBlockIndex];
        practiceBSuccessCount = 0;
        phase = SessionPhase.PracticeB_Start;
        HideProceedPanel();
        PrepareTrialPlacement(current);
        ApplyTargetLayout(current, visible: false);
        startButtonRect.gameObject.SetActive(true);
        BringTapButtonsToFront();
        SetStatus(
            $"[Practice B] Condition warm-up ({conditionBlockIndex + 1}/{conditionBlocks.Count})\n" +
            $"A={current.amplitudeMm:0.#}mm  W={current.widthMm:0.#}mm\n" +
            "Tap the green start → blue target.");
    }

    void BeginDataStartPhase()
    {
        if (conditionBlockIndex >= conditionBlocks.Count)
        {
            FinishExperiment();
            return;
        }

        current = conditionBlocks[conditionBlockIndex];
        phase = SessionPhase.Data_Start;
        HideProceedPanel();
        PrepareTrialPlacement(current);
        ApplyTargetLayout(current, visible: false);
        startButtonRect.gameObject.SetActive(true);
        BringTapButtonsToFront();
        SetStatus(
            $"[Measurement] Condition {conditionBlockIndex + 1}/{conditionBlocks.Count} · " +
            $"{trialInCondition + 1}/{trialsPerCondition}\n" +
            $"A={current.amplitudeMm:0.#}mm  W={current.widthMm:0.#}mm\n" +
            "Tap the green start button.");
    }

    void BeginTargetPhase(SessionPhase targetPhase)
    {
        phase = targetPhase;
        startButtonRect.gameObject.SetActive(false);
        ApplyTargetLayout(current, visible: true);
        stimulusTime = Time.time;
        BringTapButtonsToFront();
        SetFullscreenCatch(true); // record every tap in this phase as hit/miss

        string label = targetPhase switch
        {
            SessionPhase.PracticeA_Target => "[Practice A]",
            SessionPhase.PracticeB_Target => "[Practice B]",
            _ => "[Measurement]",
        };
        SetStatus($"{label}\nTap the blue target quickly and accurately.");
    }

    void PrepareTrialPlacement(TrialCondition cond)
    {
        currentStartPos = startAnchoredPosition;
        currentAngleDeg = movementAngleDeg;

        // Entering the start (green) waiting phase, so turn off the fullscreen catcher (so the green button tap works normally).
        SetFullscreenCatch(false);

        // Re-apply the start button size each trial. EnsureUi() runs before Start()'s await (frame 0),
        // when the canvas scaleFactor may still be 1, so a size fixed at that point ends up larger than it should be.
        ApplyStartButtonSize();

        if (randomizePlacement && rootRt != null)
            TryRandomPlacement(cond, out currentStartPos, out currentAngleDeg);

        if (startButtonRect != null)
            startButtonRect.anchoredPosition = currentStartPos;
    }

    /// <summary>Sets the start (green) button to the physical size of startButtonSizeMm (default 6mm).</summary>
    void ApplyStartButtonSize()
    {
        if (startButtonRect == null) return;
        float s = MmToCanvasUnits(startButtonSizeMm);
        startButtonRect.sizeDelta = new Vector2(s, s);
    }

    bool TryRandomPlacement(TrialCondition cond, out Vector2 startPos, out float angleDeg)
    {
        startPos = startAnchoredPosition;
        angleDeg = movementAngleDeg;

        float R = MmToCanvasUnits(cond.amplitudeMm);
        float startR = MmToCanvasUnits(startButtonSizeMm) * 0.5f;
        float targetR = MmToCanvasUnits(GetHitWidthMm(cond.widthMm)) * 0.5f;
        float pad = MmToCanvasUnits(edgePaddingMm);
        float rad = Mathf.Max(startR, targetR) + pad;

        float halfW = rootRt.rect.width * 0.5f;
        float halfH = rootRt.rect.height * 0.5f;
        float minX = -halfW + rad;
        float maxX = halfW - rad;
        float minY = -halfH + rad;
        float maxY = halfH - rad - topReserveFraction * rootRt.rect.height;

        if (maxX <= minX || maxY <= minY)
            return false;

        for (int attempt = 0; attempt < 100; attempt++)
        {
            float theta = Random.Range(0f, Mathf.PI * 2f);
            float dx = R * Mathf.Cos(theta);
            float dy = R * Mathf.Sin(theta);

            float sxLo = Mathf.Max(minX, minX - dx);
            float sxHi = Mathf.Min(maxX, maxX - dx);
            float syLo = Mathf.Max(minY, minY - dy);
            float syHi = Mathf.Min(maxY, maxY - dy);

            if (sxLo > sxHi || syLo > syHi)
                continue;

            float sx = Random.Range(sxLo, sxHi);
            float sy = Random.Range(syLo, syHi);
            startPos = new Vector2(sx, sy);
            angleDeg = theta * Mathf.Rad2Deg;
            return true;
        }

        return false;
    }

    void ApplyTargetLayout(TrialCondition cond, bool visible)
    {
        float visualCanvas = MmToCanvasUnits(cond.widthMm);
        float hitCanvas = MmToCanvasUnits(GetHitWidthMm(cond.widthMm));
        targetButtonRect.sizeDelta = new Vector2(hitCanvas, hitCanvas);
        if (targetVisualRect != null)
            targetVisualRect.sizeDelta = new Vector2(visualCanvas, visualCanvas);

        float ampCanvas = MmToCanvasUnits(cond.amplitudeMm);
        float rad = currentAngleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        targetButtonRect.anchoredPosition = currentStartPos + dir * ampCanvas;
        targetButtonRect.gameObject.SetActive(visible);
    }

    float GetHitWidthMm(float visualWMm) => visualWMm;

    float MmToCanvasUnits(float mm)
    {
        // desired physical pixel count = mm × (device px/mm)
        float physicalPx = mm * EffectiveDevicePixelsPerMm();
        // sizeDelta is in canvas-local (reference) units, so divide by scaleFactor to convert physical→local
        float scale = CanvasScaleFactor();
        return physicalPx / Mathf.Max(scale, 1e-6f);
    }

    float EffectiveDevicePixelsPerMm()
    {
        // 1) A manually specified value takes top priority (value calibrated with a ruler)
        if (pixelsPerMm > 0f) return pixelsPerMm;

        // 2) If the physical diagonal (mm) is known, calculate as 'actual render resolution diagonal pixels ÷ physical diagonal mm'.
        //    Since Screen.dpi is not trusted, this is accurate on Android devices such as Samsung,
        //    and automatically stays correct even when the resolution setting (FHD+/QHD+, etc.) changes.
        if (physicalDiagonalMm > 1f)
        {
            float w = Screen.width;
            float h = Screen.height;
            float diagPx = Mathf.Sqrt(w * w + h * h);
            if (diagPx > 1f)
                return diagPx / physicalDiagonalMm;
        }

        // 3) Last resort: Screen.dpi (may be inaccurate)
        float dpi = Screen.dpi;
        if (dpi < 1f) dpi = Mathf.Max(1f, fallbackDpi);
        return dpi / 25.4f;
    }

    float CanvasScaleFactor()
    {
        if (rootRt != null)
        {
            var c = rootRt.GetComponent<Canvas>();
            if (c != null && c.scaleFactor > 1e-6f) return c.scaleFactor;
        }
        return 1f;
    }

    public void OnCalibrationTap(FittsCalibrationTapTarget.TapRole role, Vector2 screenPos)
    {
        if (phase == SessionPhase.Finished
            || phase == SessionPhase.WaitingProceedAfterA
            || phase == SessionPhase.WaitingProceedAfterB
            || phase == SessionPhase.WaitingProceedAfterCondition)
            return;

        if (role == FittsCalibrationTapTarget.TapRole.Start)
        {
            if (phase == SessionPhase.PracticeA_Start)
            {
                PlayStartClick();
                BeginTargetPhase(SessionPhase.PracticeA_Target);
                return;
            }
            if (phase == SessionPhase.PracticeB_Start)
            {
                PlayStartClick();
                BeginTargetPhase(SessionPhase.PracticeB_Target);
                return;
            }
            if (phase == SessionPhase.Data_Start)
            {
                PlayStartClick();
                BeginTargetPhase(SessionPhase.Data_Target);
                return;
            }
            return;
        }

        if (role != FittsCalibrationTapTarget.TapRole.Target) return;

        bool hit = IsTargetHit(screenPos);
        if (!hit)
        {
            if (phase == SessionPhase.Data_Target)
            {
                float rtSec = Time.time - stimulusTime;
                RecordTrial(false, (long)(rtSec * 1000f), screenPos);
            }

            SetStatus("Miss — tap again.");
            if (IsTimedTargetPhase(phase))
                stimulusTime = Time.time;
            return;
        }

        PlayTargetSuccess();

        switch (phase)
        {
            case SessionPhase.PracticeA_Target:
                OnPracticeAComplete();
                break;
            case SessionPhase.PracticeB_Target:
                OnPracticeBHit();
                break;
            case SessionPhase.Data_Target:
                OnDataTrialHit(screenPos);
                break;
        }
    }

    static bool IsTimedTargetPhase(SessionPhase p) =>
        p == SessionPhase.PracticeA_Target || p == SessionPhase.PracticeB_Target || p == SessionPhase.Data_Target;

    void OnPracticeAComplete()
    {
        targetButtonRect.gameObject.SetActive(false);
        startButtonRect.gameObject.SetActive(false);
        SetFullscreenCatch(false);
        phase = SessionPhase.WaitingProceedAfterA;
        ShowProceedPanel(
            "Practice A complete\n\nYou are now familiar with the task flow (start → target).\nPress Continue to begin the actual measurement.",
            OnProceedAfterPracticeA);
    }

    void OnProceedAfterPracticeA()
    {
        conditionBlockIndex = 0;
        trialInCondition = 0;
        BeginPracticeB();
    }

    void OnPracticeBHit()
    {
        practiceBSuccessCount++;
        targetButtonRect.gameObject.SetActive(false);
        startButtonRect.gameObject.SetActive(false);
        SetFullscreenCatch(false);

        if (practiceBSuccessCount < Mathf.Max(1, practiceBTrialsRequired))
        {
            phase = SessionPhase.PracticeB_Start;
            PrepareTrialPlacement(current);
            ApplyTargetLayout(current, visible: false);
            startButtonRect.gameObject.SetActive(true);
            BringTapButtonsToFront();
            SetStatus(
                $"[Practice B] {practiceBSuccessCount}/{practiceBTrialsRequired} complete\n" +
                "Practice once more with the same condition.");
            return;
        }

        phase = SessionPhase.WaitingProceedAfterB;
        ShowProceedPanel(
            $"Practice B complete\n\nYou are now familiar with the A={current.amplitudeMm:0.#}mm · W={current.widthMm:0.#}mm condition.\n" +
            "Press Continue to begin the measurement trials for this condition.",
            OnProceedAfterPracticeB);
    }

    void OnProceedAfterPracticeB()
    {
        trialInCondition = 0;
        BeginDataStartPhase();
    }

    void OnDataTrialHit(Vector2 screenPos)
    {
        float rtSec = Time.time - stimulusTime;
        long reactionMs = (long)(rtSec * 1000f);
        RecordTrial(true, reactionMs, screenPos);

        trialInCondition++;
        targetButtonRect.gameObject.SetActive(false);
        SetFullscreenCatch(false);

        if (trialInCondition >= Mathf.Max(1, trialsPerCondition))
        {
            TrialCondition completed = current;
            conditionBlockIndex++;
            trialInCondition = 0;
            if (conditionBlockIndex >= conditionBlocks.Count)
            {
                FinishExperiment();
                return;
            }

            if (flowRoutine != null) StopCoroutine(flowRoutine);
            flowRoutine = StartCoroutine(ShowConditionTransitionAfterDelay(completed));
            return;
        }

        if (flowRoutine != null) StopCoroutine(flowRoutine);
        flowRoutine = StartCoroutine(NextDataTrialAfterDelay());
    }

    IEnumerator ShowConditionTransitionAfterDelay(TrialCondition completed)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, interTrialDelaySec));

        TrialCondition next = conditionBlocks[conditionBlockIndex];
        phase = SessionPhase.WaitingProceedAfterCondition;
        startButtonRect.gameObject.SetActive(false);
        targetButtonRect.gameObject.SetActive(false);

        int completedNo = conditionBlockIndex;
        int nextNo = conditionBlockIndex + 1;
        ShowProceedPanel(
            $"Condition {completedNo}/{conditionBlocks.Count} measurement complete\n\n" +
            $"The {FormatCondition(completed)} condition is finished.\n\n" +
            $"Next condition {nextNo}/{conditionBlocks.Count}: {FormatCondition(next)}\n" +
            "Press Continue to begin Practice B.",
            OnProceedAfterConditionTransition);
    }

    void OnProceedAfterConditionTransition()
    {
        BeginPracticeB();
    }

    static string FormatCondition(TrialCondition cond) =>
        $"A={cond.amplitudeMm:0.#}mm · W={cond.widthMm:0.#}mm";

    IEnumerator NextDataTrialAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, interTrialDelaySec));
        BeginDataStartPhase();
    }

    void ShowProceedPanel(string message, System.Action onProceed)
    {
        proceedCallback = onProceed;
        if (proceedMessageText != null)
            proceedMessageText.text = message;
        if (proceedPanel != null)
            proceedPanel.SetActive(true);
        if (proceedPanel != null)
            proceedPanel.transform.SetAsLastSibling();
    }

    void HideProceedPanel()
    {
        proceedCallback = null;
        if (proceedPanel != null)
            proceedPanel.SetActive(false);
    }

    void OnProceedClicked()
    {
        var cb = proceedCallback;
        HideProceedPanel();
        cb?.Invoke();
    }

    bool IsTargetHit(Vector2 screenPos)
    {
        if (!TryTouchOffsetFromTargetCenterMm(screenPos, out Vector2 offsetMm))
            return false;

        float halfHitMm = current.widthMm * 0.5f;
        return offsetMm.sqrMagnitude <= halfHitMm * halfHitMm;
    }

    bool TryTouchOffsetFromTargetCenterMm(Vector2 screenPos, out Vector2 offsetMm)
    {
        offsetMm = Vector2.zero;
        if (targetButtonRect == null) return false;

        Canvas canvas = targetButtonRect.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(targetButtonRect, screenPos, cam, out Vector2 localInHit))
            return false;

        float visualCanvasW = targetVisualRect != null && targetVisualRect.rect.width > 1e-3f
            ? targetVisualRect.rect.width
            : MmToCanvasUnits(current.widthMm);
        float mmPerCanvas = current.widthMm / Mathf.Max(visualCanvasW, 1e-3f);
        offsetMm = localInHit * mmPerCanvas;
        return true;
    }

    void RecordTrial(bool hit, long reactionMs, Vector2 screenPos)
    {
        globalTrialNo++;
        float idBase = FittsLawModel.ShannonId(current.amplitudeMm, current.widthMm);
        float idFFitts = FittsLawModel.FFittsIdSqrt(current.amplitudeMm, current.widthMm, FittsLawModel.DefaultAmbiguityC2D_mm);

        var doc = new Dictionary<string, object>
        {
            { "trial_no", globalTrialNo },
            { "condition_index", current.conditionIndex },
            { "amplitude_mm", current.amplitudeMm },
            { "target_width_mm", current.widthMm },
            { "fitts_id_baseline", idBase },
            { "fitts_id_ffitts", idFFitts },
            { "fitts_model", "calibration_param_fit" },
            { "input_mode", "tap" },
            { "is_practice", false },
            { "accuracy", hit },
            { "reaction_ms", reactionMs },
            { "n_value", 1 },
            { "created_at", Timestamp.GetCurrentTimestamp() },
        };

        if (TryScreenToRootLocal(screenPos, out Vector2 local))
        {
            doc["touch_x"] = local.x;
            doc["touch_y"] = local.y;
        }

        if (TryTouchOffsetFromTargetCenterMm(screenPos, out Vector2 offsetMm))
        {
            doc["touch_offset_x_mm"] = offsetMm.x;
            doc["touch_offset_y_mm"] = offsetMm.y;
            float halfVisual = current.widthMm * 0.5f;
            doc["accuracy_strict"] = offsetMm.magnitude <= halfVisual;
        }

        pendingDocs.Add(doc);

        if (pendingDocs.Count >= Mathf.Max(1, saveBatchSize))
            StartCoroutine(FlushPendingDocs());
    }

    bool TryScreenToRootLocal(Vector2 screenPos, out Vector2 local)
    {
        local = Vector2.zero;
        if (rootRt == null) return false;
        Canvas canvas = rootRt.GetComponent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screenPos, cam, out local);
    }

    IEnumerator FlushPendingDocs()
    {
        if (string.IsNullOrEmpty(testId) || pendingDocs.Count == 0) yield break;
        var batch = new List<Dictionary<string, object>>(pendingDocs);
        pendingDocs.Clear();
        var task = FirestoreService.I.WriteResultsBatchAsync(testId, batch);
        while (!task.IsCompleted) yield return null;
        if (task.Exception != null)
            Debug.LogError("[FittsCalibration] WriteResultsBatch failed: " + task.Exception);
    }

    void FinishExperiment()
    {
        phase = SessionPhase.Finished;
        HideProceedPanel();
        startButtonRect.gameObject.SetActive(false);
        targetButtonRect.gameObject.SetActive(false);
        SetFullscreenCatch(false);
        StartCoroutine(FinishAndShowResults());
    }

    IEnumerator FinishAndShowResults()
    {
        yield return FlushPendingDocs();
        SetStatus("Complete");
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText != null)
        {
            resultText.text =
                $"FFitts c calibration complete\n" +
                $"testId: {testId}\n" +
                $"Measurement trials: {globalTrialNo}\n\n" +
                "Estimate c:\n" +
                $"python reporting/plot_fitts_by_test_id.py --test-id {testId} --input-mode tap\n\n" +
                "Enter the printed optimized_c_mm value into\n" +
                "Length_5 → GridNumberButtonLayout → Fitts Ambiguity C Mm.\n\n" +
                "Press the 'To Main' button below to return to the start screen.";
        }
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    /// <summary>Diagnostic log to reference when calibrating physical size by measuring with a ruler on a real device.</summary>
    void LogPhysicalSizeDiagnostics()
    {
        float pxPerMm = EffectiveDevicePixelsPerMm();
        float scale = CanvasScaleFactor();
        // e.g. the number of physical pixels a W=10mm circle should occupy on screen
        float samplePx = 10f * pxPerMm;
        Debug.Log(
            $"[FittsCalibration] Physical size diagnostics | " +
            $"Screen={Screen.width}x{Screen.height}px, Screen.dpi={Screen.dpi:0.#}, " +
            $"physicalDiagonalMm={physicalDiagonalMm:0.##}, calculated px/mm={pxPerMm:0.###}, " +
            $"canvasScaleFactor={scale:0.###} | " +
            $"W=10mm → {samplePx:0.#} physical px (=sizeDelta {samplePx / Mathf.Max(scale, 1e-6f):0.#}). " +
            $"If it doesn't match when measured with a ruler, enter a value directly into pixelsPerMm (corrected by the ratio of measured mm to intended mm).");
    }
}
