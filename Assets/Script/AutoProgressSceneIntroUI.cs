using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Guide UI for auto-sequence mode (shown once at scene start; the same component can be reused when proceeding to the "Next Test" after a test ends).
/// Place it under a Canvas and connect the message / continue button in the inspector.
/// If absent from the scene, it falls back to the <see cref="AutoProgressIntroOverlay"/> runtime UI.
///
/// Sequence: (1) "~" announcement message -> (2) play the explanation video -> (3) show the start button once the video ends.
/// In the inspector you only need to connect the per-scene Intro Video Clip. The VideoPlayer, RenderTexture, and display RawImage
/// are created automatically at runtime (if you made your own, those are used). If the clip is left empty, the video step is skipped.
/// </summary>
[DisallowMultipleComponent]
public class AutoProgressSceneIntroUI : MonoBehaviour
{
    [Tooltip("Root to disable at start (if empty, the object this component is attached to)")]
    [SerializeField] GameObject visibilityRoot;
    [SerializeField] Text messageLegacy;
    [SerializeField] TMP_Text messageTmp;
    [Tooltip("Start (continue) button shown only after the video ends")]
    [SerializeField] Button proceedButton;

    [Header("Explanation video (connect per scene)")]
    [Tooltip("Explanation video for this scene's test. Connect only this and the code auto-creates the VideoPlayer, RenderTexture, and display RawImage. If left empty, the video step is skipped and the start button appears right after the announcement.")]
    [SerializeField] VideoClip introVideoClip;
    [Tooltip("(Optional) Connect if you made your own VideoPlayer. If empty, the code auto-creates one.")]
    [SerializeField] VideoPlayer videoPlayer;
    [Tooltip("(Optional) Connect if you made your own video display area (RawImage, etc.). If empty, the code auto-creates one at the center of the screen.")]
    [SerializeField] GameObject videoDisplayRoot;
    [Tooltip("How long (seconds) to show only the announcement. After this time the video plays.")]
    [SerializeField] float announcementSeconds = 1.5f;
    [Tooltip("How the video fills the screen. Off (default): keep aspect ratio so the whole video is visible (letterbox, possible top/bottom or left/right margins). On: fill the screen fully but crop the overflow.")]
    [SerializeField] bool autoVideoFillCrop = false;

    [Header("After test ends -> next scene")]
    [Tooltip("Text to put in Message when the same panel is shown after one scene ends in auto-sequence. Even after the start intro has overwritten the text, it is refilled with this value. Change the continue button label (START, etc.) directly on the button's child Text.")]
    [SerializeField] [TextArea(2, 5)] private string messageAfterTestBeforeNextScene =
        "Press the button below to move on to the next test.";

    TaskCompletionSource<bool> _tcs;
    Coroutine _sequenceRoutine;
    bool _videoDone;

    // Auto-created display resources (when not connected directly in the inspector)
    RenderTexture _autoRenderTexture;
    RawImage _autoRawImage;
    AspectRatioFitter _autoAspectFitter;

    void Awake()
    {
        if (visibilityRoot == null)
            visibilityRoot = gameObject;
        if (proceedButton != null)
            proceedButton.onClick.AddListener(OnProceedClicked);
        HideImmediate();
    }

    void OnProceedClicked()
    {
        HideImmediate();
        _tcs?.TrySetResult(true);
    }

    void HideImmediate()
    {
        StopSequence();
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.Stop();
        }
        if (videoDisplayRoot != null)
            videoDisplayRoot.SetActive(false);
        SetProceedActive(false);
        if (visibilityRoot != null)
            visibilityRoot.SetActive(false);
    }

    void StopSequence()
    {
        if (_sequenceRoutine != null)
        {
            StopCoroutine(_sequenceRoutine);
            _sequenceRoutine = null;
        }
    }

    void SetProceedActive(bool on)
    {
        if (proceedButton != null)
            proceedButton.gameObject.SetActive(on);
    }

    void BringIntroToFront()
    {
        if (visibilityRoot == null) return;
        visibilityRoot.transform.SetAsLastSibling();
        var canvas = visibilityRoot.GetComponentInParent<Canvas>();
        if (canvas != null)
            canvas.transform.SetAsLastSibling();
    }

    /// <param name="message">If null or an empty string, Message Legacy/TMP is left untouched (keeps the text set in the scene).</param>
    public Task WaitUntilProceedAsync(string message)
    {
        return BeginIntroSequence(message);
    }

    /// <summary>
    /// Auto-sequence: when moving to the next scene after the current one ends. Overwrites the message (which the start intro had changed) with the field below or the code string.
    /// </summary>
    /// <param name="optionalDynamicFromChain">Used only when HickExperiment passes a chain title. If empty, messageAfterTestBeforeNextScene is used.</param>
    public Task WaitUntilProceedForNextTestAsync(string optionalDynamicFromChain)
    {
        string body = !string.IsNullOrEmpty(optionalDynamicFromChain)
            ? optionalDynamicFromChain
            : messageAfterTestBeforeNextScene;
        if (string.IsNullOrEmpty(body))
            body = "Press the button below to move on to the next test.";
        return BeginIntroSequence(body);
    }

    /// <summary>
    /// Immediately shows only the message + continue (start) button without waiting for video/announcement, and waits until the button is clicked.
    /// Used when reusing the scene start intro panel as-is, e.g. 'Start Main Session' after practice is done.
    /// </summary>
    public Task WaitUntilProceedImmediateAsync(string message)
    {
        _tcs = new TaskCompletionSource<bool>();

        if (!string.IsNullOrEmpty(message))
        {
            if (messageLegacy != null) messageLegacy.text = message;
            if (messageTmp != null) messageTmp.text = message;
        }

        StopSequence();
        if (videoDisplayRoot != null)
            videoDisplayRoot.SetActive(false);

        if (visibilityRoot != null)
        {
            visibilityRoot.SetActive(true);
            BringIntroToFront();
        }

        SetProceedActive(true);   // Show the start button immediately without a video
        return _tcs.Task;
    }

    Task BeginIntroSequence(string message)
    {
        _tcs = new TaskCompletionSource<bool>();

        if (!string.IsNullOrEmpty(message))
        {
            if (messageLegacy != null)
                messageLegacy.text = message;
            if (messageTmp != null)
                messageTmp.text = message;
        }

        // If a video is connected, prepare the display resources (VideoPlayer/RenderTexture/RawImage)
        EnsureVideoDisplay();

        // Initial state: show only the announcement, hide the video and start button
        SetProceedActive(false);
        if (videoDisplayRoot != null)
            videoDisplayRoot.SetActive(false);

        if (visibilityRoot != null)
        {
            visibilityRoot.SetActive(true);
            BringIntroToFront();
        }

        StopSequence();
        _sequenceRoutine = StartCoroutine(RunIntroSequence());
        return _tcs.Task;
    }

    IEnumerator RunIntroSequence()
    {
        // 1) "~" announcement step
        if (announcementSeconds > 0f)
            yield return new WaitForSeconds(announcementSeconds);

        // 2) Explanation video step (only when connected)
        if (videoPlayer != null && introVideoClip != null)
        {
            if (videoDisplayRoot != null)
                videoDisplayRoot.SetActive(true);

            _videoDone = false;
            videoPlayer.isLooping = false;
            videoPlayer.clip = introVideoClip;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.loopPointReached += OnVideoFinished;
            videoPlayer.Play();

            // Wait until the video finishes playing (the start button stays hidden meanwhile)
            while (!_videoDone)
                yield return null;

            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.Stop();
        }

        // 3) Close the video area and show the start button (so it isn't overlapped/hidden by the button)
        if (videoDisplayRoot != null)
            videoDisplayRoot.SetActive(false);
        SetProceedActive(true);
        _sequenceRoutine = null;
    }

    void OnVideoFinished(VideoPlayer vp)
    {
        _videoDone = true;
    }

    /// <summary>
    /// If introVideoClip is connected but the VideoPlayer/display area are empty,
    /// auto-creates and connects the RenderTexture, RawImage, and VideoPlayer at runtime.
    /// (If connected directly in the inspector, those are used as-is)
    /// </summary>
    void EnsureVideoDisplay()
    {
        if (introVideoClip == null) return;
        // If both are connected directly, do not auto-create
        if (videoPlayer != null && videoDisplayRoot != null) return;

        // Prepare the display RawImage (full screen + keep aspect ratio)
        if (videoDisplayRoot == null)
        {
            Transform parent = visibilityRoot != null ? visibilityRoot.transform : transform;
            var go = new GameObject("IntroVideoDisplay",
                typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            go.transform.SetParent(parent, false);

            // Fill the parent (full screen). The actual size is adjusted to the aspect ratio by AspectRatioFitter.
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _autoRawImage = go.GetComponent<RawImage>();
            _autoRawImage.raycastTarget = false; // So it doesn't block button clicks

            _autoAspectFitter = go.GetComponent<AspectRatioFitter>();
            _autoAspectFitter.aspectMode = autoVideoFillCrop
                ? AspectRatioFitter.AspectMode.EnvelopeParent   // Fill the screen and crop the overflow
                : AspectRatioFitter.AspectMode.FitInParent;     // Keep aspect ratio so the whole video is visible (letterbox)

            videoDisplayRoot = go;
        }
        else
        {
            if (_autoRawImage == null)
            {
                _autoRawImage = videoDisplayRoot.GetComponent<RawImage>();
                if (_autoRawImage == null)
                    _autoRawImage = videoDisplayRoot.GetComponentInChildren<RawImage>(true);
            }
            if (_autoAspectFitter == null)
                _autoAspectFitter = videoDisplayRoot.GetComponent<AspectRatioFitter>();
        }

        // Prepare the VideoPlayer (Unity's == overload treats missing components as null too)
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
        }

        // Prepare the RenderTexture (matching the clip resolution, or 1280x720 if unavailable)
        int w = introVideoClip.width  > 0 ? (int)introVideoClip.width  : 1280;
        int h = introVideoClip.height > 0 ? (int)introVideoClip.height : 720;
        if (_autoRenderTexture == null || _autoRenderTexture.width != w || _autoRenderTexture.height != h)
        {
            if (_autoRenderTexture != null)
                _autoRenderTexture.Release();
            _autoRenderTexture = new RenderTexture(w, h, 0);
            _autoRenderTexture.Create();
        }

        // Apply the video's original aspect ratio to the fitter (prevents distortion)
        if (_autoAspectFitter != null && h > 0)
            _autoAspectFitter.aspectRatio = (float)w / h;

        videoPlayer.playOnAwake = false;
        videoPlayer.source = VideoSource.VideoClip;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = _autoRenderTexture;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;

        if (_autoRawImage != null)
            _autoRawImage.texture = _autoRenderTexture;
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoFinished;
        if (_autoRenderTexture != null)
        {
            _autoRenderTexture.Release();
            _autoRenderTexture = null;
        }
    }
}
