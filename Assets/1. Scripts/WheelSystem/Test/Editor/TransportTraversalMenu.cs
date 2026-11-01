using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TransportTraversalMenu
{
    private const string Armed = "TransportTraversal.Armed";
    private const string Clock = "TransportTraversal.Clock";
    private const string StartZ = "TransportTraversal.StartZ", EndZ = "TransportTraversal.EndZ";
    static TransportTraversalMenu() { EditorApplication.playModeStateChanged += OnPlayMode; }
    [MenuItem("Tools/Transport Traversal/Run From Start")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before a fresh run.");
        SessionState.SetBool(Armed, true);
        EditorApplication.isPlaying = true;
    }
    [MenuItem("Tools/Transport Traversal/Run From Start (Normal Clock)")]
    public static void RunNormal() { SessionState.SetFloat(Clock, 1f); Run(); }
    public static void RunSection(float startZ, float endZ)
    {
        SessionState.SetFloat(StartZ, startZ); SessionState.SetFloat(EndZ, endZ); Run();
    }
    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Armed, false)) return;
        SessionState.SetBool(Armed, false);
        Application.runInBackground = true;
        float clock = SessionState.GetFloat(Clock, 0f);
        SessionState.SetFloat(Clock, 0f);
        float start = SessionState.GetFloat(StartZ, 0f), end = SessionState.GetFloat(EndZ, 0f);
        SessionState.SetFloat(StartZ, 0f); SessionState.SetFloat(EndZ, 0f);
        TransportTraversalTest.StartTest(clock, start, end);
    }
}
