using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainSceneController : MonoBehaviour
{
    public enum SessionSegment
    {
        RightHandedAuto = 1,
        SingleFromType = 2,
    }

    [SerializeField] private InputField nameInput;
    [SerializeField] private Dropdown typeDropdown;
    [SerializeField] private Dropdown sexDropdown;
    [SerializeField] private InputField ageInput;

    [Header("Segment buttons (RIGHT / ETC)")]
    [Tooltip("Assign both, like the mockup UI. ETC = single scene via the Type dropdown.")]
    [SerializeField] private Button segmentRightButton;
    [SerializeField] private Button segmentEtcButton;
    [SerializeField] private Color segmentNormalBackground = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private Color segmentNormalLabel = Color.white;
    [SerializeField] private Color segmentSelectedBackground = Color.white;
    [SerializeField] private Color segmentSelectedLabel = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private SessionSegment initialSegment = SessionSegment.SingleFromType;

    [Header("Segment (optional) legacy dropdown")]
    [Tooltip("Used only when both buttons are left empty. Option order: 1=RIGHT, 2=ETC.")]
    [SerializeField] private Dropdown sessionSegmentDropdown;

    [Header("Run")]
    [Tooltip("OnClick can be left empty.")]
    [SerializeField] private Button sessionSelectButton;

    [System.Serializable]
    public class TypeToScene
    {
        public string typeLabel;
        public string sceneName;
    }

    [Header("Scene Map")]
    [SerializeField] private List<TypeToScene> sceneMap = new();
    [SerializeField] private string fallbackSceneName = "Length_5";

    SessionSegment currentSegment;

    bool UsesSegmentButtons =>
        segmentRightButton != null || segmentEtcButton != null;

    private void Start()
    {
        currentSegment = initialSegment;

        if (segmentRightButton != null)
            segmentRightButton.onClick.AddListener(() => SetSegment(SessionSegment.RightHandedAuto));
        if (segmentEtcButton != null)
            segmentEtcButton.onClick.AddListener(() => SetSegment(SessionSegment.SingleFromType));

        if (sessionSelectButton != null)
            sessionSelectButton.onClick.AddListener(RunSelectedSession);

        RefreshSegmentVisuals();
    }

    void SetSegment(SessionSegment segment)
    {
        currentSegment = segment;
        RefreshSegmentVisuals();
    }

    void RefreshSegmentVisuals()
    {
        StyleSegmentButton(segmentRightButton, currentSegment == SessionSegment.RightHandedAuto);
        StyleSegmentButton(segmentEtcButton, currentSegment == SessionSegment.SingleFromType);
    }

    void StyleSegmentButton(Button btn, bool selected)
    {
        if (btn == null) return;

        btn.transition = Selectable.Transition.None;

        var bg = btn.targetGraphic as Image;
        if (bg != null)
            bg.color = selected ? segmentSelectedBackground : segmentNormalBackground;

        Color labelColor = selected ? segmentSelectedLabel : segmentNormalLabel;

        var texts = btn.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
            texts[i].color = labelColor;

        var tmpTexts = btn.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmpTexts.Length; i++)
            tmpTexts[i].color = labelColor;
    }

    SessionSegment GetActiveSegment()
    {
        if (UsesSegmentButtons)
            return currentSegment;
        if (sessionSegmentDropdown != null)
            return (SessionSegment)Mathf.Clamp(sessionSegmentDropdown.value, 1, 2);
        return SessionSegment.SingleFromType;
    }

    public void RunSelectedSession()
    {
        if (!ApplyFormToConfig())
            return;
        if (ExperimentConfig.I == null)
            return;

        SessionSegment seg = GetActiveSegment();

        if (seg == SessionSegment.RightHandedAuto)
        {
            ExperimentConfig.I.testType = "Right-Handed Auto-Sequence";
            ExperimentConfig.I.BeginRightHandedAutoProgress();
            if (ExperimentConfig.I.autoProgressQueue.Count == 0)
            {
                Debug.LogError("[Main] Right-hand auto queue is empty.");
                return;
            }
            SceneManager.LoadScene(ExperimentConfig.I.autoProgressQueue[0]);
            return;
        }

        ExperimentConfig.I.ClearAutoProgress();
        string type = GetSelectedTypeLabel();
        ExperimentConfig.I.testType = type;
        string sceneName = ResolveSceneNameForType(type);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[Main] sceneName is empty. Check Scene Map / fallbackSceneName");
            return;
        }

        Debug.Log($"[Main] specific mode type='{type}' -> scene='{sceneName}'");
        SceneManager.LoadScene(sceneName);
    }

    public void StartExperiment()
    {
        if (!ApplyFormToConfig())
            return;

        if (ExperimentConfig.I != null)
            ExperimentConfig.I.ClearAutoProgress();

        string type = GetSelectedTypeLabel();
        string sceneName = ResolveSceneNameForType(type);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[Main] sceneName is empty. Check Scene Map / fallbackSceneName");
            return;
        }

        Debug.Log($"[Main] type='{type}' -> scene='{sceneName}'");
        SceneManager.LoadScene(sceneName);
    }

    bool ApplyFormToConfig()
    {
        if (ExperimentConfig.I == null)
        {
            Debug.LogError("ExperimentConfig is not in the scene. Create a Config object and attach ExperimentConfig.");
            return false;
        }

        string type = GetSelectedTypeLabel();

        ExperimentConfig.I.userName = (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text))
            ? nameInput.text.Trim()
            : "guest";

        ExperimentConfig.I.testType = type;

        ExperimentConfig.I.sex = (sexDropdown != null && sexDropdown.options.Count > 0)
            ? (sexDropdown.options[sexDropdown.value].text ?? "").Trim()
            : "unknown";

        if (!int.TryParse(ageInput != null ? ageInput.text : null, out ExperimentConfig.I.age))
            ExperimentConfig.I.age = 0;

        return true;
    }

    string GetSelectedTypeLabel()
    {
        if (typeDropdown != null && typeDropdown.options != null && typeDropdown.options.Count > 0)
            return (typeDropdown.options[typeDropdown.value].text ?? "").Trim();
        return "";
    }

    string ResolveSceneNameForType(string type)
    {
        string sceneName = (fallbackSceneName ?? "").Trim();
        for (int i = 0; i < sceneMap.Count; i++)
        {
            var m = sceneMap[i];
            if (m == null) continue;

            string label = (m.typeLabel ?? "").Trim();
            string mappedScene = (m.sceneName ?? "").Trim();

            if (label.Length == 0 || mappedScene.Length == 0) continue;

            if (string.Equals(label, type, System.StringComparison.Ordinal))
            {
                sceneName = mappedScene;
                break;
            }
        }

        return sceneName;
    }
}
