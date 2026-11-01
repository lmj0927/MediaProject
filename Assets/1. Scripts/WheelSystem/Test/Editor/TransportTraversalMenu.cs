using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TransportTraversalMenu
{
    private const string Armed = "TransportTraversal.Armed";
    static TransportTraversalMenu() { EditorApplication.playModeStateChanged += OnPlayMode; }
    [MenuItem("Tools/Transport Traversal/Run From Start")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before a fresh run.");
        SessionState.SetBool(Armed, true);
        EditorApplication.isPlaying = true;
    }
    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Armed, false)) return;
        SessionState.SetBool(Armed, false);
        Application.runInBackground = true;
        TransportTraversalTest.StartTest();
    }
}
