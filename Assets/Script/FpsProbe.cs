using UnityEngine;

/// <summary>
/// A diagnostic overlay that measures/displays the actual render frame rate on the device.
/// Usage: add this component to a GameObject in any scene → build/run → check the value at
/// the top-left of the screen. (Or check via the "[FpsProbe]" tag in adb logcat.)
/// Remove the component once measurement is done.
/// </summary>
public class FpsProbe : MonoBehaviour
{
    [Tooltip("Logs the average fps to the console (logcat) every this many seconds.")]
    [SerializeField] private float logIntervalSec = 1f;

    float smoothedFps;         // exponential moving average (perceived fps)
    float minFps = float.MaxValue;
    float maxFps;
    int frameCount;
    float windowTime;          // accumulated over the log interval
    float windowFrames;        // number of frames within the log interval

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        float fps = 1f / dt;
        // the first frame spikes, so start aggregating min/max after a few frames
        smoothedFps = smoothedFps <= 0f ? fps : Mathf.Lerp(smoothedFps, fps, 0.1f);
        frameCount++;
        if (frameCount > 10)
        {
            if (fps < minFps) minFps = fps;
            if (fps > maxFps) maxFps = fps;
        }

        windowTime += dt;
        windowFrames += 1f;
        if (windowTime >= Mathf.Max(0.1f, logIntervalSec))
        {
            float avg = windowFrames / windowTime;
            Debug.Log($"[FpsProbe] avg={avg:0.0}fps  smoothed={smoothedFps:0.0}  " +
                      $"min={minFps:0.0}  max={maxFps:0.0}  " +
                      $"display={GetRefreshHz():0.0}Hz  target={Application.targetFrameRate}  vSync={QualitySettings.vSyncCount}");
            windowTime = 0f;
            windowFrames = 0f;
        }
    }

    static float GetRefreshHz()
    {
#if UNITY_2022_2_OR_NEWER
        var r = Screen.currentResolution.refreshRateRatio;
        return r.denominator == 0 ? 0f : (float)(r.numerator / (double)r.denominator);
#else
        return Screen.currentResolution.refreshRate;
#endif
    }

    void OnGUI()
    {
        float instFps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
        var style = new GUIStyle
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.028f),
            normal = { textColor = Color.yellow }
        };
        float pad = Screen.height * 0.02f;
        string text =
            $"FPS now:{instFps:0.0}  smooth:{smoothedFps:0.0}\n" +
            $"min:{(minFps == float.MaxValue ? 0f : minFps):0.0}  max:{maxFps:0.0}\n" +
            $"display:{GetRefreshHz():0.0}Hz\n" +
            $"targetFrameRate:{Application.targetFrameRate}\n" +
            $"vSyncCount:{QualitySettings.vSyncCount}\n" +
            $"1/fps ≈ {(smoothedFps > 0f ? 1000f / smoothedFps : 0f):0.0} ms";
        // shadow for readability
        var shadow = new GUIStyle(style); shadow.normal.textColor = Color.black;
        GUI.Label(new Rect(pad + 2, pad + 2, Screen.width, Screen.height), text, shadow);
        GUI.Label(new Rect(pad, pad, Screen.width, Screen.height), text, style);
    }
}
