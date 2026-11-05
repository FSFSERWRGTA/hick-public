using UnityEngine;

/// <summary>
/// Forces the render resolution to the Galaxy S24 Ultra QHD+ (3120×1440) at app startup.
/// Physical size accuracy (px/mm) is calibrated from the actual resolution by
/// FittsCalibrationExperiment's automatic diagonal calculation anyway, but rendering at
/// QHD+ packs the pixels per mm more densely (≈19.89px/mm), so target sizes and touch
/// positions are represented more precisely.
///
/// Note: For the buffer to actually become QHD+, the Android [Settings → Display → Screen
/// resolution] must be set to 'WQHD+ (3120×1440)'. If the system is FHD+, the OS may not
/// scale it up any higher (even in that case the physical size itself is auto-calibrated
/// and remains accurate).
/// </summary>
public static class DisplayResolutionBootstrap
{
    // Galaxy S24 Ultra native panel resolution
    const int QhdLongSide = 3120;
    const int QhdShortSide = 1440;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ForceQhdPlusOnDevice()
    {
        // In the editor/PC the window shrinks oddly, so only apply on a real device
        if (Application.platform != RuntimePlatform.Android)
            return;

        bool portrait = Screen.height >= Screen.width;
        int width = portrait ? QhdShortSide : QhdLongSide;
        int height = portrait ? QhdLongSide : QhdShortSide;

        // Skip re-setting if already at that resolution
        if (Screen.width == width && Screen.height == height)
            return;

        Screen.SetResolution(width, height, true);
        Debug.Log($"[DisplayResolutionBootstrap] QHD+ requested: {width}x{height} (previous {Screen.width}x{Screen.height}). " +
                  "Whether it actually takes effect depends on the OS display resolution setting.");
    }
}
