using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasScaler))]
public class ResponsiveCanvasScaler : MonoBehaviour
{
    [Header("Reference Resolution")]
    [SerializeField] private Vector2 portraitReference = new Vector2(1080f, 1920f);
    [SerializeField] private Vector2 landscapeReference = new Vector2(1920f, 1080f);

    [Header("CanvasScaler Match")]
    [Range(0f, 1f)]
    [SerializeField] private float portraitMatch = 1f;
    [Range(0f, 1f)]
    [SerializeField] private float landscapeMatch = 0f;

    [Header("Extra Shrink On Small Screens")]
    [SerializeField] private bool shrinkUiOnSmallScreens = true;
    [SerializeField] private RectTransform scaleTarget;
    [Range(0.6f, 1f)]
    [SerializeField] private float minScale = 0.82f;

    private CanvasScaler canvasScaler;
    private int prevWidth = -1;
    private int prevHeight = -1;

    private void Awake()
    {
        canvasScaler = GetComponent<CanvasScaler>();
        if (scaleTarget == null)
        {
            scaleTarget = transform as RectTransform;
        }
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        if (prevWidth == Screen.width && prevHeight == Screen.height)
        {
            return;
        }
        Apply();
    }

    private void Apply()
    {
        prevWidth = Screen.width;
        prevHeight = Screen.height;

        bool isPortrait = Screen.height >= Screen.width;
        Vector2 reference = isPortrait ? portraitReference : landscapeReference;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = reference;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = isPortrait ? portraitMatch : landscapeMatch;

        if (!shrinkUiOnSmallScreens || scaleTarget == null)
        {
            return;
        }

        // Only on small screens, slightly shrink the entire UI to reduce clipping.
        float widthRatio = Screen.width / reference.x;
        float heightRatio = Screen.height / reference.y;
        float shrinkScale = Mathf.Min(widthRatio, heightRatio);
        shrinkScale = Mathf.Clamp(shrinkScale, minScale, 1f);
        scaleTarget.localScale = new Vector3(shrinkScale, shrinkScale, 1f);
    }
}
