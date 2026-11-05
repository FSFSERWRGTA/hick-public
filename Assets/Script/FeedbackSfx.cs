using UnityEngine;

/// <summary>
/// Plays correct ("ding") / incorrect ("buzz") sound effects globally across the app.
/// The waveform is generated in code without any audio files, so no separate assets are needed.
/// It is created automatically on first call and persists across scene changes (DontDestroyOnLoad).
/// </summary>
public class FeedbackSfx : MonoBehaviour
{
    const int SampleRate = 44100;

    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.7f;

    private static FeedbackSfx instance;
    private AudioSource source;
    private AudioClip correctClip;
    private AudioClip wrongClip;

    public static FeedbackSfx I
    {
        get
        {
            if (instance == null)
            {
                var existing = FindFirstObjectByType<FeedbackSfx>();
                if (existing != null)
                {
                    instance = existing;
                }
                else
                {
                    var go = new GameObject("FeedbackSfx");
                    instance = go.AddComponent<FeedbackSfx>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureReady();
    }

    private void EnsureReady()
    {
        if (source == null)
        {
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        if (correctClip == null) correctClip = BuildCorrectChime();
        if (wrongClip == null) wrongClip = BuildWrongBeep();
    }

    /// <summary>"ding" if correct, "buzz" if incorrect.</summary>
    public static void Play(bool correct)
    {
        if (correct) PlayCorrect();
        else PlayWrong();
    }

    public static void PlayCorrect()
    {
        var s = I;
        s.EnsureReady();
        s.source.PlayOneShot(s.correctClip, s.volume);
    }

    public static void PlayWrong()
    {
        var s = I;
        s.EnsureReady();
        s.source.PlayOneShot(s.wrongClip, s.volume);
    }

    /// <summary>"ding" — a rising chime of two connected notes (988Hz → 1319Hz).</summary>
    private static AudioClip BuildCorrectChime()
    {
        const float dur = 0.34f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float s = 0f;
            if (t < 0.20f)
                s += Mathf.Sin(2f * Mathf.PI * 988f * t) * Mathf.Exp(-t * 12f);
            float t2 = t - 0.10f;                     // the second note starts overlapping
            if (t2 > 0f)
                s += Mathf.Sin(2f * Mathf.PI * 1319f * t2) * Mathf.Exp(-t2 * 9f);
            data[i] = s * 0.5f;
        }
        var clip = AudioClip.Create("SfxCorrect", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>"buzz" — two short beeps (620Hz, a slightly edgy timbre).</summary>
    private static AudioClip BuildWrongBeep()
    {
        const float dur = 0.26f;
        int n = Mathf.RoundToInt(SampleRate * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float local;
            if (t < 0.08f) local = t;                 // first "beep"
            else if (t >= 0.12f && t < 0.20f) local = t - 0.12f;  // second "beep"
            else { data[i] = 0f; continue; }

            // sine + 3rd harmonic for an electronic feel
            float w = Mathf.Sin(2f * Mathf.PI * 620f * local)
                    + 0.3f * Mathf.Sin(2f * Mathf.PI * 1860f * local);
            data[i] = w * Mathf.Exp(-local * 18f) * 0.42f;
        }
        var clip = AudioClip.Create("SfxWrong", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
