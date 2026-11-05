using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ResultReportLauncher : MonoBehaviour
{
    private const string GeneratedReportUrlResource = "FirebaseReportUrl";

    private enum LaunchMode
    {
        ExternalBrowser,
        UniWebViewPanel
    }

    [Header("Report URL")]
    [Tooltip("Leave empty to use FIREBASE_REPORT_URL from .env. e.g.: https://your-report-app.web.app/result")]
    [SerializeField] private string reportBaseUrl = "";
    [Tooltip("If true, append testId as a path (/result/{id}); if false, append it as a query (?testId=...)")]
    [SerializeField] private bool usePathStyle = false;
    [SerializeField] private string testIdQueryKey = "testId";

    [Header("Optional UI")]
    [Tooltip("The URL is shown in Report Url Text only when this is on. When off, it is left empty so text like ?testId=… does not appear large in the background.")]
    [SerializeField] private bool showReportUrlInLabel;
    [SerializeField] private Text reportUrlText;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private bool openOnSetTestId = true;
    [SerializeField] private LaunchMode launchMode = LaunchMode.ExternalBrowser;

    [Header("UniWebView (Optional)")]
    [SerializeField] private Rect webViewRect = new Rect(0, 120, 1080, 1700);
    [SerializeField] private bool hideResultPanelWhileWebViewVisible = true;
    [SerializeField] private GameObject panelToHide;

    private string currentTestId;
    private string currentUrl;
    private Component uniWebViewComponent;
    private GameObject uniWebViewGameObject;

    private void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(OpenReport);
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseReport);
        RefreshUi();
    }

    public void SetTestId(string testId)
    {
        currentTestId = testId;
        currentUrl = BuildUrl(testId);
        RefreshUi();

        if (openOnSetTestId)
            OpenReport();
    }

    /// <summary>After applying the testId, always opens the browser (or UniWebView) once. For the middle segment of an auto-continuous run, etc.</summary>
    public void PublishTestAndOpenReport(string testId)
    {
        currentTestId = testId;
        currentUrl = BuildUrl(testId);
        RefreshUi();
        OpenReport();
    }

    public void OpenReport()
    {
        if (string.IsNullOrWhiteSpace(currentUrl))
        {
            Debug.LogWarning("[ResultReportLauncher] No URL to open. Check FIREBASE_REPORT_URL in .env or testId.");
            return;
        }

        if (launchMode == LaunchMode.UniWebViewPanel && TryOpenWithUniWebView(currentUrl))
            return;

        Application.OpenURL(currentUrl);
    }

    public void CloseReport()
    {
        CloseUniWebView();
        if (hideResultPanelWhileWebViewVisible && panelToHide != null)
            panelToHide.SetActive(true);
    }

    /// <summary>A URL set on the component wins; otherwise the one generated from .env at build time.</summary>
    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(reportBaseUrl))
            return reportBaseUrl;

        TextAsset generatedUrl = Resources.Load<TextAsset>(GeneratedReportUrlResource);
        return generatedUrl != null ? generatedUrl.text : "";
    }

    private string BuildUrl(string testId)
    {
        string baseUrl = ResolveBaseUrl().Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "";
        if (string.IsNullOrWhiteSpace(testId))
            return baseUrl;

        if (usePathStyle)
            return $"{baseUrl}/{UnityWebRequestEscape(testId)}";

        string key = string.IsNullOrWhiteSpace(testIdQueryKey) ? "testId" : testIdQueryKey.Trim();
        return $"{baseUrl}?{key}={UnityWebRequestEscape(testId)}";
    }

    private void RefreshUi()
    {
        if (reportUrlText == null) return;
        if (showReportUrlInLabel)
            reportUrlText.text = string.IsNullOrWhiteSpace(currentUrl) ? "No report URL" : currentUrl;
        else
            reportUrlText.text = "";
    }

    private bool TryOpenWithUniWebView(string url)
    {
        Type uniType = FindTypeByName("UniWebView");
        if (uniType == null)
        {
            Debug.LogWarning("[ResultReportLauncher] Could not find the UniWebView type. Install the plugin and try again.");
            return false;
        }

        if (uniWebViewGameObject == null)
        {
            uniWebViewGameObject = new GameObject("UniWebViewRuntime");
            uniWebViewComponent = uniWebViewGameObject.AddComponent(uniType);
        }
        else if (uniWebViewComponent == null)
        {
            uniWebViewComponent = uniWebViewGameObject.GetComponent(uniType);
        }

        if (uniWebViewComponent == null)
        {
            Debug.LogWarning("[ResultReportLauncher] Failed to initialize the UniWebView component");
            return false;
        }

        SetMember(uniWebViewComponent, "Frame", webViewRect);
        InvokeIfExists(uniWebViewComponent, "SetShowToolbar", false);
        InvokeIfExists(uniWebViewComponent, "Load", url);
        InvokeIfExists(uniWebViewComponent, "Show");

        if (hideResultPanelWhileWebViewVisible && panelToHide != null)
            panelToHide.SetActive(false);
        return true;
    }

    private void CloseUniWebView()
    {
        if (uniWebViewComponent != null)
            InvokeIfExists(uniWebViewComponent, "Hide");
        if (uniWebViewGameObject != null)
            Destroy(uniWebViewGameObject);
        uniWebViewComponent = null;
        uniWebViewGameObject = null;
    }

    private static Type FindTypeByName(string typeName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == typeName);
    }

    private static void SetMember(object obj, string name, object value)
    {
        Type t = obj.GetType();
        var prop = t.GetProperty(name);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
            return;
        }
        var field = t.GetField(name);
        if (field != null)
            field.SetValue(obj, value);
    }

    private static void InvokeIfExists(object obj, string method, params object[] args)
    {
        var methods = obj.GetType().GetMethods().Where(m => m.Name == method).ToList();
        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length != args.Length) continue;
            m.Invoke(obj, args);
            return;
        }
    }

    private static string UnityWebRequestEscape(string input)
    {
        return Uri.EscapeDataString(input ?? "");
    }

    private void OnDestroy()
    {
        CloseUniWebView();
    }
}
