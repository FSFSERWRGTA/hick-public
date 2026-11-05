using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Generates the Firebase client config from the git-ignored .env in the project root, so no
/// project-specific values are committed. Unity cannot read .env inside a built app, so the
/// values are written out when the project loads and again before every build. The Firebase SDK
/// derives its Android resources and desktop config from google-services.json on its own.
/// </summary>
[InitializeOnLoad]
public class FirebaseEnvConfig : IPreprocessBuildWithReport
{
    private const string EnvPath = ".env";
    private const string AndroidConfigPath = "Assets/google-services.json";
    private const string IosConfigPath = "Assets/GoogleService-Info.plist";
    private const string ReportUrlPath = "Assets/Resources/FirebaseReportUrl.txt";

    private const string PackageNameKey = "FIREBASE_ANDROID_PACKAGE_NAME";
    private const string BundleIdKey = "FIREBASE_IOS_BUNDLE_ID";
    private const string ReportUrlKey = "FIREBASE_REPORT_URL";
    private const string PlaceholderPrefix = "your-";

    private const string EnvMissingMessage =
        "[Firebase] .env not found. Copy .env.example to .env and fill in your Firebase project values (see README).";
    private const string WarnedSessionKey = "FirebaseEnvConfig.Warned";

    private static readonly string[] SharedKeys =
    {
        "FIREBASE_PROJECT_ID",
        "FIREBASE_PROJECT_NUMBER",
        "FIREBASE_STORAGE_BUCKET",
    };

    private static readonly string[] AndroidKeys = SharedKeys
        .Concat(new[] { PackageNameKey, "FIREBASE_ANDROID_APP_ID", "FIREBASE_ANDROID_API_KEY" })
        .ToArray();

    private static readonly string[] IosKeys = SharedKeys
        .Concat(new[] { BundleIdKey, "FIREBASE_IOS_APP_ID", "FIREBASE_IOS_API_KEY" })
        .ToArray();

    private const string AndroidConfigTemplate = @"{
  ""project_info"": {
    ""project_number"": ""${FIREBASE_PROJECT_NUMBER}"",
    ""project_id"": ""${FIREBASE_PROJECT_ID}"",
    ""storage_bucket"": ""${FIREBASE_STORAGE_BUCKET}""
  },
  ""client"": [
    {
      ""client_info"": {
        ""mobilesdk_app_id"": ""${FIREBASE_ANDROID_APP_ID}"",
        ""android_client_info"": {
          ""package_name"": ""${FIREBASE_ANDROID_PACKAGE_NAME}""
        }
      },
      ""oauth_client"": [],
      ""api_key"": [
        {
          ""current_key"": ""${FIREBASE_ANDROID_API_KEY}""
        }
      ],
      ""services"": {
        ""appinvite_service"": {
          ""other_platform_oauth_client"": []
        }
      }
    }
  ],
  ""configuration_version"": ""1""
}
";

    private const string IosConfigTemplate = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>API_KEY</key>
	<string>${FIREBASE_IOS_API_KEY}</string>
	<key>GCM_SENDER_ID</key>
	<string>${FIREBASE_PROJECT_NUMBER}</string>
	<key>PLIST_VERSION</key>
	<string>1</string>
	<key>BUNDLE_ID</key>
	<string>${FIREBASE_IOS_BUNDLE_ID}</string>
	<key>PROJECT_ID</key>
	<string>${FIREBASE_PROJECT_ID}</string>
	<key>STORAGE_BUCKET</key>
	<string>${FIREBASE_STORAGE_BUCKET}</string>
	<key>IS_ADS_ENABLED</key>
	<false></false>
	<key>IS_ANALYTICS_ENABLED</key>
	<false></false>
	<key>IS_APPINVITE_ENABLED</key>
	<true></true>
	<key>IS_GCM_ENABLED</key>
	<true></true>
	<key>IS_SIGNIN_ENABLED</key>
	<true></true>
	<key>GOOGLE_APP_ID</key>
	<string>${FIREBASE_IOS_APP_ID}</string>
</dict>
</plist>
";

    // Runs before the Firebase SDK's own pre-build step, which reads google-services.json.
    public int callbackOrder => int.MinValue;

    static FirebaseEnvConfig()
    {
        EditorApplication.delayCall += ApplyOnProjectLoad;
    }

    [MenuItem("Tools/Firebase/Apply .env")]
    private static void ApplyFromMenu()
    {
        Dictionary<string, string> env = ReadEnv();
        if (env == null)
        {
            Debug.LogError(EnvMissingMessage);
            return;
        }

        WriteConfigFiles(env);
        Debug.Log("[Firebase] Applied .env");
    }

    private static void ApplyOnProjectLoad()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Dictionary<string, string> env = ReadEnv();
        if (env != null)
        {
            WriteConfigFiles(env);
        }
        else if (!SessionState.GetBool(WarnedSessionKey, false))
        {
            SessionState.SetBool(WarnedSessionKey, true);
            Debug.LogWarning(EnvMissingMessage);
        }
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        Dictionary<string, string> env = ReadEnv();
        if (env == null) throw new BuildFailedException(EnvMissingMessage);

        BuildTarget target = report.summary.platform;
        bool isIos = target == BuildTarget.iOS;

        List<string> missing = MissingKeys(env, isIos ? IosKeys : AndroidKeys);
        if (missing.Count > 0)
            throw new BuildFailedException("[Firebase] Set these values in .env before building: " + string.Join(", ", missing));

        if (isIos || target == BuildTarget.Android)
        {
            string expected = env[isIos ? BundleIdKey : PackageNameKey];
            string actual = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup));
            if (expected != actual)
                throw new BuildFailedException($"[Firebase] .env is registered for '{expected}' but Player Settings uses '{actual}'. Make them match.");
        }

        WriteConfigFiles(env);
    }

    private static void WriteConfigFiles(Dictionary<string, string> env)
    {
        bool changed = false;

        if (MissingKeys(env, AndroidKeys).Count == 0)
            changed |= WriteIfChanged(AndroidConfigPath, Fill(AndroidConfigTemplate, env));

        if (MissingKeys(env, IosKeys).Count == 0)
            changed |= WriteIfChanged(IosConfigPath, Fill(IosConfigTemplate, env));

        env.TryGetValue(ReportUrlKey, out string reportUrl);
        changed |= WriteIfChanged(ReportUrlPath, reportUrl ?? "");

        if (changed) AssetDatabase.Refresh();
    }

    /// <summary>Returns the key/value pairs in .env, or null when the file does not exist.</summary>
    private static Dictionary<string, string> ReadEnv()
    {
        if (!File.Exists(EnvPath)) return null;

        var env = new Dictionary<string, string>();
        foreach (string rawLine in File.ReadAllLines(EnvPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ")) line = line.Substring("export ".Length);

            int separator = line.IndexOf('=');
            if (separator <= 0) continue;

            env[line.Substring(0, separator).Trim()] = ParseValue(line.Substring(separator + 1).Trim());
        }

        return env;
    }

    private static string ParseValue(string value)
    {
        bool isQuoted = value.Length >= 2
            && (value[0] == '"' || value[0] == '\'')
            && value[value.Length - 1] == value[0];
        if (isQuoted) return value.Substring(1, value.Length - 2);

        int comment = value.IndexOf(" #");
        return comment >= 0 ? value.Substring(0, comment).TrimEnd() : value;
    }

    private static List<string> MissingKeys(Dictionary<string, string> env, IEnumerable<string> keys)
    {
        return keys
            .Where(key => !env.TryGetValue(key, out string value)
                || value.Length == 0
                || value.StartsWith(PlaceholderPrefix))
            .ToList();
    }

    private static string Fill(string template, Dictionary<string, string> env)
    {
        return Regex
            .Replace(template, @"\$\{(\w+)\}", match => env[match.Groups[1].Value])
            .Replace("\r\n", "\n");
    }

    private static bool WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return true;
    }
}
