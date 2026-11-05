using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the app to build/hick.apk. From the command line:
/// Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod AndroidApkBuild.Build
/// </summary>
public static class AndroidApkBuild
{
    private const string OutputPath = "build/hick.apk";

    [MenuItem("Tools/Build Android APK")]
    public static void Build()
    {
        EditorUserBuildSettings.buildAppBundle = false;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.CompressWithLz4,
        });

        bool succeeded = report.summary.result == BuildResult.Succeeded;
        Debug.Log($"[AndroidApkBuild] {report.summary.result}: {OutputPath} ({report.summary.totalSize / (1024 * 1024)} MB)");

        if (Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
    }
}
