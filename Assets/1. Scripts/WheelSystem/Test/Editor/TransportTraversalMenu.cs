using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TransportTraversalMenu
{
    private const string Armed = "TransportTraversal.Armed";
    private const string Clock = "TransportTraversal.Clock";
    private const string Record = "TransportTraversal.Record";
    private const string ContactProbe = "TransportTraversal.ContactProbe";
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
    [MenuItem("Tools/Transport Traversal/Record From Start (Normal Clock)")]
    public static void RecordNormal()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before a fresh run.");
        if (!System.IO.File.Exists("C:/ffmpeg/bin/ffmpeg.exe")) throw new System.IO.FileNotFoundException("Recording needs C:/ffmpeg/bin/ffmpeg.exe");
        SessionState.SetBool(Record, true);
        RunNormal();
    }
    [MenuItem("Tools/Transport Traversal/Check Rotating Contacts")]
    public static void CheckRotatingContacts()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before contact checks.");
        SessionState.SetBool(ContactProbe, true);
        Run();
    }
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
        bool contactProbe = SessionState.GetBool(ContactProbe, false);
        SessionState.SetBool(ContactProbe, false);
        if (contactProbe) TransportTraversalTest.Active.gameObject.AddComponent<RotationContactProbe>();
        bool record = SessionState.GetBool(Record, false);
        SessionState.SetBool(Record, false);
        if (record) {
            var test = TransportTraversalTest.Active;
            test.gameObject.AddComponent<TraversalVideoRecorder>().Begin(test);
        }
    }
}
