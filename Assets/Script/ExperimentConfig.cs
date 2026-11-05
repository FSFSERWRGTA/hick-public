using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ExperimentConfig : MonoBehaviour
{
    public enum AutoProgressKind
    {
        None,
        RightHanded,
    }

    public static ExperimentConfig I { get; private set; }

    [Header("From Main Scene")]
    public string userName;
    public string testType;
    public string sex;
    public int age;

    [Header("Auto progress (4-scene chain)")]
    public AutoProgressKind autoProgressKind = AutoProgressKind.None;
    public List<string> autoProgressQueue = new();
    public int autoProgressIndex;

    private static readonly string[] RightHandedChain = { "right_r", "right_r_s", "hick", "Length_5" };

    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    public void BeginRightHandedAutoProgress()
    {
        BeginAutoProgress(AutoProgressKind.RightHanded, RightHandedChain);
    }

    void BeginAutoProgress(AutoProgressKind kind, IReadOnlyList<string> scenes)
    {
        autoProgressKind = kind;
        autoProgressQueue.Clear();
        for (int i = 0; i < scenes.Count; i++)
            autoProgressQueue.Add(scenes[i]);
        ShuffleSceneList(autoProgressQueue);
        autoProgressIndex = 0;
    }

    static void ShuffleSceneList(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public void ClearAutoProgress()
    {
        autoProgressKind = AutoProgressKind.None;
        autoProgressQueue.Clear();
        autoProgressIndex = 0;
    }

    public bool ShouldShowSceneIntro(out string message)
    {
        message = null;
        if (autoProgressKind == AutoProgressKind.None || autoProgressQueue.Count == 0) return false;
        if (autoProgressIndex < 0 || autoProgressIndex >= autoProgressQueue.Count) return false;
        string expected = autoProgressQueue[autoProgressIndex];
        if (SceneManager.GetActiveScene().name != expected) return false;
        message = $"This is the {AutoProgressSceneTitles.TitleFor(expected)} test.";
        return true;
    }

    /// <summary>Whether there is still a next scene remaining during auto-progress (before incrementing the index).</summary>
    public bool PeekHasNextAutoScene()
    {
        if (autoProgressKind == AutoProgressKind.None || autoProgressQueue == null || autoProgressQueue.Count == 0)
            return false;
        return autoProgressIndex + 1 < autoProgressQueue.Count;
    }

    /// <summary>The name of the scene that will load next (the next slot in the queue), before moving the index.</summary>
    public bool TryPeekNextAutoSceneName(out string nextSceneName)
    {
        nextSceneName = null;
        if (autoProgressKind == AutoProgressKind.None || autoProgressQueue == null || autoProgressQueue.Count == 0)
            return false;
        int n = autoProgressIndex + 1;
        if (n >= autoProgressQueue.Count) return false;
        nextSceneName = autoProgressQueue[n];
        return true;
    }

    /// <summary>Called after the current scene ends: if there is a next scene, move the index and return the name to load.</summary>
    public bool TryAdvanceAutoProgressChain(out string nextScene)
    {
        nextScene = null;
        if (autoProgressKind == AutoProgressKind.None || autoProgressQueue.Count == 0) return false;
        int n = autoProgressIndex + 1;
        if (n >= autoProgressQueue.Count) return false;
        autoProgressIndex = n;
        nextScene = autoProgressQueue[n];
        return true;
    }
}