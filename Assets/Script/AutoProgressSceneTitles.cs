using System.Collections.Generic;

public static class AutoProgressSceneTitles
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        ["right_r"] = "Radial-Tap",
        ["right_r_s"] = "Radial-Swipe",
        ["hick"] = "Baseline",
        ["Length_5"] = "Grid",
        ["Fitts_C_Calib"] = "FFitts c Calibration",
    };

    public static string TitleFor(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return sceneName;
        return Titles.TryGetValue(sceneName.Trim(), out var t) ? t : sceneName;
    }
}
