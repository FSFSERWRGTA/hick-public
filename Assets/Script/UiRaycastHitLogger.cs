using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// In Play mode, prints the GraphicRaycast chain at screen coordinates to the console.
/// Attach it to an empty GameObject; the scene just needs an EventSystem + Canvas.
/// </summary>
public class UiRaycastHitLogger : MonoBehaviour
{
    [SerializeField] private bool logMouse = true;
    [SerializeField] private bool logTouches = true;
    [Tooltip("Minimum interval (seconds) to reduce multiple logs within a single frame")]
    [SerializeField] private float minLogInterval = 0.05f;

    private float _lastLogTime;

    private void Update()
    {
        if (EventSystem.current == null)
            return;

        if (logMouse && Input.GetMouseButtonDown(0))
            TryLogHits(Input.mousePosition);

        if (logTouches && Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
                TryLogHits(t.position);
        }
    }

    private void TryLogHits(Vector2 screenPos)
    {
        if (Time.unscaledTime - _lastLogTime < minLogInterval)
            return;
        _lastLogTime = Time.unscaledTime;

        var ped = new PointerEventData(EventSystem.current) { position = screenPos };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        var sb = new StringBuilder();
        sb.AppendLine($"[UiRaycastHitLogger] screen={screenPos} hits={results.Count} (0=topmost, receives input first)");
        for (int i = 0; i < results.Count; i++)
        {
            RaycastResult r = results[i];
            sb.AppendLine($"  [{i}] depth={r.depth} module={r.module?.name} -> {HierarchyPath(r.gameObject.transform)}");
        }

        Debug.Log(sb.ToString());
    }

    private static string HierarchyPath(Transform t)
    {
        if (t == null) return "";
        var stack = new Stack<string>();
        for (Transform c = t; c != null; c = c.parent)
            stack.Push(c.name);
        return string.Join("/", stack);
    }
}
