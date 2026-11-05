using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class NumberButton : MonoBehaviour, IPointerClickHandler
{
    [Tooltip("Fixed in the scene/prefab. The experiment logic reacts to this value, and the code does not change the label.")]
    [SerializeField] private int logicalNumber = 1;

    public int Number => logicalNumber;

    private Button btn;
    private System.Action<int, Vector2> onPressed;

    void Awake()
    {
        // Prefab mistake: if NumberButton is on the root but is also attached to a child Text,
        // EnsureButton on the child creates an empty Button and the Text's raycast is hit first from above,
        // so events fail to reach the parent Button/RegisterCallback (especially in the narrow "1" slot).
        if (transform.parent != null
            && transform.parent.GetComponent<NumberButton>() != null
            && GetComponent<Text>() != null)
        {
            var t = GetComponent<Text>();
            if (t != null) t.raycastTarget = false;
            var orphan = GetComponent<Button>();
            if (orphan != null) Destroy(orphan);
            enabled = false;
            return;
        }

        EnsureButton();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        logicalNumber = Mathf.Max(1, logicalNumber);
    }
#endif

    void EnsureButton()
    {
        if (btn == null) btn = GetComponent<Button>();
        if (btn == null) btn = gameObject.AddComponent<Button>();
    }

    /// <summary>Connects only the experiment callback. Does not change logicalNumber or the UI text.</summary>
    public void RegisterCallback(System.Action<int, Vector2> onPressed)
    {
        this.onPressed = onPressed;
        EnsureButton();
        btn.onClick.RemoveAllListeners();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (btn == null || !btn.interactable) return;
        onPressed?.Invoke(logicalNumber, eventData.position);
    }

    public void SetInteractable(bool on)
    {
        EnsureButton();
        btn.interactable = on;
    }
}
