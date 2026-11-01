#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

/// <summary>Editor-only recording of the actual traversal, using an independent follow camera.</summary>
[DefaultExecutionOrder(2000)]
public class TraversalVideoRecorder : MonoBehaviour
{
    private const int Width = 1280, Height = 720, Fps = 15;
    private TransportTraversalTest test;
    private Transform target;
    private Camera view;
    private RenderTexture surface;
    private Texture2D pixels;
    private Process encoder;
    private Stream input;
    private Stopwatch clock;
    private int frames;
    private bool stopped;
    private string directory;

    public void Begin(TransportTraversalTest traversal)
    {
        test = traversal;
        directory = test.RunDirectory;
        target = FindFirstObjectByType<SphereDriver>().transform;
        view = new GameObject("Traversal recording camera (runtime only)").AddComponent<Camera>();
        view.CopyFrom(Camera.main);
        view.enabled = false;
        view.fieldOfView = 60f;
        surface = new RenderTexture(Width, Height, 24);
        view.targetTexture = surface;
        pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        encoder = new Process { StartInfo = new ProcessStartInfo {
            FileName = "C:/ffmpeg/bin/ffmpeg.exe",
            Arguments = "-y -loglevel error -f rawvideo -pixel_format rgb24 -video_size 1280x720 -framerate 15 -i pipe:0 -vf vflip -an -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart \"" + Path.Combine(directory, "full_traversal.mp4") + "\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true
        }};
        encoder.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) File.AppendAllText(Path.Combine(directory, "video-encoder.log"), e.Data + Environment.NewLine); };
        encoder.Start();
        encoder.BeginErrorReadLine();
        input = encoder.StandardInput.BaseStream;
        clock = Stopwatch.StartNew();
    }

    private void LateUpdate()
    {
        if (stopped || clock == null) return;
        try {
            int due = (int)(clock.Elapsed.TotalSeconds * Fps) + 1;
            if (frames < due) {
                Vector3 p = target.position;
                view.transform.position = p + new Vector3(25f, 27f, -38f);
                view.transform.LookAt(p + new Vector3(0f, 3f, 12f));
                var previous = RenderTexture.active;
                try {
                    view.Render();
                    RenderTexture.active = surface;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    var data = pixels.GetRawTextureData<byte>().ToArray();
                    while (frames < due) { input.Write(data, 0, data.Length); frames++; }
                }
                finally { RenderTexture.active = previous; }
            }
            if (test.Result != "Starting" && test.Result != "Running") StopRecording();
        }
        catch (Exception e) {
            UnityEngine.Debug.LogError("Traversal recording failed: " + e);
            if (test.Result == "Running") test.Complete("RecordingFailed");
            StopRecording();
        }
    }

    private void StopRecording()
    {
        if (stopped) return;
        stopped = true;
        clock?.Stop();
        input?.Dispose();
        if (encoder != null) {
            if (!encoder.WaitForExit(10000)) { encoder.Kill(); UnityEngine.Debug.LogError("Traversal encoder did not finish."); }
            File.WriteAllText(Path.Combine(directory, "video.txt"),
                "1280x720, 15 fps, continuous wall-clock recording, no audio\nFrames: " + frames +
                "\nWall seconds: " + clock.Elapsed.TotalSeconds + "\nSimulation seconds: " + test.Elapsed +
                "\nResult: " + test.Result + "\nEncoder exit: " + encoder.ExitCode);
            encoder.Dispose();
        }
        if (view != null) Destroy(view.gameObject);
        if (surface != null) { surface.Release(); Destroy(surface); }
        if (pixels != null) Destroy(pixels);
    }

    private void OnDestroy() { StopRecording(); }
}

#endif
