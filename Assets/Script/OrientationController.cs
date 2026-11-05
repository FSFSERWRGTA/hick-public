using UnityEngine;

public class OrientationController : MonoBehaviour
{
    public enum Mode
    {
        Portrait,
        LandscapeLeft,
        LandscapeRight,
        LandscapeAuto
    }

    [SerializeField] private Mode mode = Mode.LandscapeAuto;
    [SerializeField] private bool restoreOnDisable = false;
    [SerializeField] private bool logWhenSuppressed = true;

    private ScreenOrientation prevOrientation;
    private bool prevAutoRotateToPortrait;
    private bool prevAutoRotateToPortraitUpsideDown;
    private bool prevAutoRotateToLandscapeLeft;
    private bool prevAutoRotateToLandscapeRight;

    private void OnEnable()
    {
        // Back up the previous settings
        prevOrientation = Screen.orientation;
        prevAutoRotateToPortrait = Screen.autorotateToPortrait;
        prevAutoRotateToPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
        prevAutoRotateToLandscapeLeft = Screen.autorotateToLandscapeLeft;
        prevAutoRotateToLandscapeRight = Screen.autorotateToLandscapeRight;

        if (ShouldSuppressByHigherPriorityController())
        {
            if (logWhenSuppressed)
            {
                Debug.Log($"[OrientationController] '{name}' ({mode}) suppressed by higher-priority controller in scene.");
            }
            return;
        }

        Apply(mode);
    }

    private void OnDisable()
    {
        // On scene transitions, the OnDisable order can overwrite the next scene's settings, so by default we do not restore.
        if (!restoreOnDisable) return;

        Screen.orientation = prevOrientation;
        Screen.autorotateToPortrait = prevAutoRotateToPortrait;
        Screen.autorotateToPortraitUpsideDown = prevAutoRotateToPortraitUpsideDown;
        Screen.autorotateToLandscapeLeft = prevAutoRotateToLandscapeLeft;
        Screen.autorotateToLandscapeRight = prevAutoRotateToLandscapeRight;
    }

    private void Apply(Mode m)
    {
        switch (m)
        {
            case Mode.Portrait:
                Screen.autorotateToPortrait = true;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = false;
                Screen.autorotateToLandscapeRight = false;
                Screen.orientation = ScreenOrientation.Portrait;
                break;

            case Mode.LandscapeLeft:
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = false;
                Screen.orientation = ScreenOrientation.LandscapeLeft;
                break;

            case Mode.LandscapeRight:
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = false;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.LandscapeRight;
                break;

            case Mode.LandscapeAuto:
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
                break;
        }
    }

    private bool ShouldSuppressByHigherPriorityController()
    {
        var controllers = FindObjectsByType<OrientationController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            var other = controllers[i];
            if (other == null || other == this || !other.isActiveAndEnabled) continue;

            // Auto has the lowest priority: if the same scene has an explicit orientation (Portrait/Landscape), do not apply.
            if (mode == Mode.LandscapeAuto && other.mode != Mode.LandscapeAuto) return true;
        }
        return false;
    }
}
