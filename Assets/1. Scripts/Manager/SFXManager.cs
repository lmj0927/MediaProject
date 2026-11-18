using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal enum SfxCue : byte
{
    Button,
    Popup,
    Collision,
    Throw,
    Stamp,
    Landing = 6,
    Grab,
    WaterFall,
    Checkpoint
}

/// <summary>Owns persistent playback for music, UI, and gameplay audio.</summary>
public sealed class SFXManager : MonoBehaviour
{
    private const int WorldVoiceCount = 8;
    private const string VolumePreference = "RollWithUs.Sfx";
    private const string MusicPreference = "RollWithUs.Music";

    private static readonly string[] ClipNames =
    {
        "SFX_UI_Button_Click", "SFX_UI_Popup_Open_V2", "SFX_Collision_V2",
        "SFX_Throw", "SFX_Stamp", null, "SFX_Landing",
        "SFX_Grab", "SFX_Water_Fall", "SFX_Checkpoint"
    };

    public static SFXManager Instance { get; private set; }

    private AudioClip[] clips;
    private AudioSource uiVoice;
    private AudioSource musicVoice;
    private AudioSource speedVoice;
    private AudioClip lobbyMusic;
    private AudioClip matchMusic;
    private TransportSpeedFeel speedOwner;
    private float speedIntensity;
    private float speedPitch = .82f;
    private float speedGain;
    private float sfxVolume;
    private readonly AudioSource[] worldVoices = new AudioSource[WorldVoiceCount];
    private int nextVoice;
    private SfxCue? pendingUi;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
            new GameObject(nameof(SFXManager)).AddComponent<SFXManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        clips = new AudioClip[ClipNames.Length];
        for (int i = 0; i < ClipNames.Length; i++)
            if (ClipNames[i] != null)
                clips[i] = Resources.Load<AudioClip>("SFX/" + ClipNames[i]);

        uiVoice = CreateVoice("UI", 0f);
        for (int i = 0; i < worldVoices.Length; i++)
            worldVoices[i] = CreateVoice($"World {i + 1}", .65f);

        musicVoice = CreateVoice("Background Music", 0f);
        musicVoice.loop = true;
        musicVoice.priority = 48;
        lobbyMusic = Resources.Load<AudioClip>("Music/BGM_Main_Lobby");
        matchMusic = Resources.Load<AudioClip>("Music/BGM_InGame");

        speedVoice = CreateVoice("Transport Speed Loop", 0f);
        speedVoice.clip = Resources.Load<AudioClip>("SFX/SFX_Acceleration");
        speedVoice.loop = true;
        speedVoice.pitch = speedPitch;
        ApplyVolumes();

        SceneManager.sceneLoaded += BindSceneButtons;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start() => SelectMusic(SceneManager.GetActiveScene());

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= BindSceneButtons;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Instance = null;
    }

    private AudioSource CreateVoice(string voiceName, float spatialBlend)
    {
        var voice = new GameObject(voiceName).AddComponent<AudioSource>();
        voice.transform.SetParent(transform, false);
        voice.playOnAwake = false;
        voice.spatialBlend = spatialBlend;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 12f;
        voice.maxDistance = 45f;
        return voice;
    }

    private static void BindSceneButtons(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Main" && scene.name != "Lobby" && scene.name != "FactoryMap")
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Button button in root.GetComponentsInChildren<Button>(includeInactive: true))
                BindButton(button);
    }

    private void OnActiveSceneChanged(Scene previous, Scene current) => SelectMusic(current);

    private void SelectMusic(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        AudioClip next = null;
        if (scene.name == "Main" || scene.name == "Lobby")
            next = lobbyMusic;
        else if (scene.name == "FactoryMap")
            next = matchMusic;
        else
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<TransportSpeedFeel>(includeInactive: true) != null)
                {
                    next = matchMusic;
                    break;
                }

        if (musicVoice.clip == next) return;
        musicVoice.Stop();
        musicVoice.clip = next;
        if (next != null)
            musicVoice.Play();
    }

    internal static void RefreshVolumes()
    {
        if (Instance != null) Instance.ApplyVolumes();
    }

    private void ApplyVolumes()
    {
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreference, 1f));
        musicVoice.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicPreference, 1f));
        uiVoice.volume = sfxVolume;
        speedVoice.volume = speedGain * sfxVolume;
        foreach (AudioSource voice in worldVoices)
            voice.volume = sfxVolume;
    }

    internal static void SetTransportSpeed(TransportSpeedFeel owner, float intensity, float pitchProgress)
    {
        if (Instance == null) return;
        if (intensity > SpeedStreaksGraphic.VisibleIntensity)
        {
            Instance.speedOwner = owner;
            Instance.speedIntensity = intensity;
            Instance.speedPitch = Mathf.Lerp(.82f, 1.65f, pitchProgress);
        }
        else if (Instance.speedOwner == owner)
        {
            Instance.speedOwner = null;
            Instance.speedIntensity = 0f;
        }
    }

    private void UpdateSpeedLoop()
    {
        if (speedVoice.clip == null) return;
        float targetGain = speedOwner != null ? .34f * Mathf.Sqrt(speedIntensity) : 0f;
        float dt = Time.unscaledDeltaTime;
        speedGain = Mathf.MoveTowards(speedGain, targetGain, dt * 1.8f);
        speedVoice.volume = speedGain * sfxVolume;
        speedVoice.pitch = Mathf.Lerp(speedVoice.pitch, speedPitch, 1f - Mathf.Exp(-dt * 12f));
        if (speedGain > 0f && !speedVoice.isPlaying)
            speedVoice.Play();
        else if (speedGain == 0f && speedVoice.isPlaying)
            speedVoice.Stop();
    }

    internal static void BindButton(Button button)
    {
        button.onClick.RemoveListener(QueueButton);
        button.onClick.AddListener(QueueButton);
    }

    private static void QueueButton()
    {
        if (Instance != null)
            Instance.pendingUi ??= SfxCue.Button;
    }

    internal static void PopupOpened()
    {
        if (Instance != null)
            Instance.pendingUi = SfxCue.Popup;
    }

    private void LateUpdate()
    {
        UpdateSpeedLoop();
        // A button that opened a modal uses the popup cue for that interaction.
        if (!pendingUi.HasValue) return;
        Play(uiVoice, pendingUi.Value, 1f);
        pendingUi = null;
    }

    internal static void PlayWorld(SfxCue cue, Vector3 position, float intensity = 1f)
    {
        if (Instance != null)
            Instance.PlayAt(cue, position, intensity);
    }

    private void PlayAt(SfxCue cue, Vector3 position, float intensity)
    {
        for (int i = 0; i < worldVoices.Length; i++)
        {
            int index = (nextVoice + i) % worldVoices.Length;
            AudioSource voice = worldVoices[index];
            if (voice.isPlaying) continue;
            voice.transform.position = position;
            Play(voice, cue, intensity);
            nextVoice = (index + 1) % worldVoices.Length;
            return;
        }
    }

    private void Play(AudioSource voice, SfxCue cue, float intensity)
    {
        AudioClip clip = clips[(int)cue];
        if (clip != null && sfxVolume > 0f)
            voice.PlayOneShot(clip, Mathf.Clamp01(intensity));
    }
}
