using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class FirestoreSmokeTest : MonoBehaviour
{
    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(t =>
        {
            if (t.Result != DependencyStatus.Available)
            {
                Debug.LogError("Firebase deps not available: " + t.Result);
                return;
            }

            var db = FirebaseFirestore.DefaultInstance;
            db.Collection("smoke").Document("ping").SetAsync(new Dictionary<string, object>
            {
                {"ok", true},
                {"ts", Timestamp.GetCurrentTimestamp()}
            }).ContinueWithOnMainThread(w =>
            {
                if (w.IsFaulted) Debug.LogError(w.Exception);
                else Debug.Log("Write OK");
            });
        });
    }
}