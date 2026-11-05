using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using UnityEngine;

public class FirestoreService : MonoBehaviour
{
    public static FirestoreService I { get; private set; }

    private FirebaseFirestore db;
    public bool Ready { get; private set; }

    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        await InitAsync();
    }

    public async Task InitAsync()
    {
        if (Ready) return;

        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            Debug.LogError("Firebase dependency error: " + dep);
            Ready = false;
            return;
        }

        db = FirebaseFirestore.DefaultInstance;

        // Anonymous auth: sign in anonymously each session to satisfy security rules (request.auth != null).
        // Continue even on failure (log only) — if rules are open (if true) it still works; if rules require auth, it gets filtered out at the write step.
        try
        {
            var auth = Firebase.Auth.FirebaseAuth.DefaultInstance;
            if (auth.CurrentUser == null)
                await auth.SignInAnonymouslyAsync();
            Debug.Log("Anonymous auth ready. uid=" + (auth.CurrentUser != null ? auth.CurrentUser.UserId : "null"));
        }
        catch (Exception e)
        {
            Debug.LogError("Anonymous sign-in failed (writes may be denied if rules require auth): " + e);
        }

        Ready = true;
        Debug.Log("Firestore ready.");
    }

    public async Task<string> CreateTestInfoAsync(string name, string type, string sex, int age, string sceneName = null, string sceneTitle = null)
    {
        if (!Ready) await InitAsync();
        if (!Ready) throw new Exception("Firestore not ready");

        var doc = new Dictionary<string, object>
        {
            { "name", name },
            { "type", type },
            { "sex", sex },
            { "age", age },
            { "scene", sceneName ?? string.Empty },
            { "scene_title", sceneTitle ?? string.Empty },
            { "created_at", Timestamp.GetCurrentTimestamp() },
            { "updated_at", Timestamp.GetCurrentTimestamp() },
        };

        DocumentReference created = await db.Collection("test_info").AddAsync(doc);
        return created.Id;
    }

    public async Task WriteResultsBatchAsync(string testId, List<Dictionary<string, object>> trialDocs)
    {
        if (!Ready) await InitAsync();
        if (!Ready) throw new Exception("Firestore not ready");

        var testDocRef = db.Collection("test_info").Document(testId);
        var batch = db.StartBatch();

        for (int i = 0; i < trialDocs.Count; i++)
        {
            // Save to the subcollection with an auto-id
            var rref = testDocRef.Collection("test_result").Document();
            batch.Set(rref, trialDocs[i]);
        }

        // Update updated_at
        batch.Update(testDocRef, new Dictionary<string, object>
        {
            { "updated_at", Timestamp.GetCurrentTimestamp() }
        });

        await batch.CommitAsync();
    }
}