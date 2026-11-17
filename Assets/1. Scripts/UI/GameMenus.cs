using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameMenus : MonoBehaviour
{
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private bool inGame;

    private const string PreferencePrefix = "RollWithUs.";
    private static GameMenus activeModal;
    private RectTransform canvas;
    private RectTransform overlay;
    private RectTransform card;
    private CanvasGroup scrimFade;
    private CanvasGroup background;
    private bool backgroundWasInteractable;
    private bool backgroundBlockedRaycasts;
    private GameObject previousSelection;
    private Button gear;
    private TMP_Dropdown resolutionSelect;
    private bool returnToSettings;
    private GameUiSprites Sprites => GameUiSprites.Shared;

    public static bool BlocksPlayerInput => activeModal != null;

    private void Start()
    {
        AudioListener.volume = Volume("Master");
        SetMixer("Music", Volume("Music"));
        SetMixer("Sfx", Volume("Sfx"));
        SetMixer("Ui", Volume("Sfx"));
        if (TryGetComponent<RoomTitleView>(out _) && (PlayerPrefs.HasKey(PreferencePrefix + "ScreenWidth") ||
            PlayerPrefs.HasKey(PreferencePrefix + "Fullscreen")))
            ApplyDisplay();
        if (inGame)
            BuildGear();
    }

    private void OnDisable() => Close();

    private void Update()
    {
        if (scrimFade != null)
            scrimFade.alpha = Mathf.MoveTowards(scrimFade.alpha, 1f, Time.unscaledDeltaTime * 8f);
        if (activeModal != this || Keyboard.current?.escapeKey.wasPressedThisFrame != true)
            return;
        if (resolutionSelect != null && resolutionSelect.transform.Find("Dropdown List") != null)
            resolutionSelect.Hide();
        else
            Dismiss();
    }

    public void ShowSettings()
    {
        Begin("Settings", new Vector2(inGame ? 690f : 634f, 798f), inGame ? -2f : -8f, !inGame);
        var close = ButtonAt("Close", inGame ? 261f : 236f, inGame ? 344f : 342f, inGame ? 74f : 65f, 68f,
            inGame ? Sprites.ButtonSky : Sprites.ButtonCream, "", Dismiss);
        GameUi.Picture(Child("Close icon", close.transform, 0, 2, 27, 27), Sprites.Close);
        TextAt("Title", "설정", inGame ? 0f : -172f, inGame ? 346f : 342f, 180f, 74f, inGame ? 68 : 65,
            inGame ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft);

        Section("소리", inGame ? 137f : 123f, inGame ? 267f : 238f);
        VolumeRow("전체 음량", "Master", inGame ? 198f : 175f);
        VolumeRow("배경음악", "Music", inGame ? 129f : 113f);
        VolumeRow("효과음", "Sfx", inGame ? 58f : 51f);
        Section("화면", inGame ? -151f : -139f, inGame ? 207f : 192f);
        DisplayRows();

        if (inGame)
        {
            var exit = ButtonAt("Leave match", -159, -329, 294, 86, Sprites.ButtonDanger, "게임 나가기", ShowMatchLeave);
            exit.GetComponentInChildren<TMP_Text>().color = GameUi.Coral;
        }
        var done = ButtonAt("Done", inGame ? 159f : 0f, inGame ? -329f : -316f, inGame ? 294f : 546f, inGame ? 89f : 94f,
            Sprites.ButtonGold, "닫기", Dismiss);
        done.Select();
    }

    public void ShowRoomLeave() => ConfirmLeave(false);

    private void ShowMatchLeave() => ConfirmLeave(true);

    private void ConfirmLeave(bool match)
    {
        Begin("Confirm leave", new Vector2(624f, 270f), match ? 0f : 72f, false);
        returnToSettings = match;
        TextAt("Question", match ? "게임에서 나갈까요?" : "방에서 나갈까요?",
            0, 60, 560, 78, 53, TextAlignmentOptions.Center);
        ButtonAt("Continue", -138, -47, 250, 87, Sprites.ButtonSky, "계속하기", Dismiss).Select();
        ButtonAt("Leave", 138, -47, 250, 87, Sprites.ButtonCoral, "나가기", () =>
        {
            Close();
            if (RoomSession.Active != null)
                RoomSession.Active.Leave();
            else if (inGame)
                SceneManager.LoadScene(0);
        });
    }

    private void Dismiss()
    {
        if (returnToSettings)
            ShowSettings();
        else
            Close();
    }

    private void Begin(string panelName, Vector2 size, float y, bool yellowHeader)
    {
        EnsureCanvas();
        if (activeModal == null)
        {
            activeModal = this;
            previousSelection = EventSystem.current?.currentSelectedGameObject;
            if (TryGetComponent<Canvas>(out _))
            {
                if (!TryGetComponent<CanvasGroup>(out background))
                    background = gameObject.AddComponent<CanvasGroup>();
                backgroundWasInteractable = background.interactable;
                backgroundBlockedRaycasts = background.blocksRaycasts;
                background.interactable = false;
                background.blocksRaycasts = false;
            }
            if (gear != null) gear.interactable = false;
        }

        if (overlay != null)
        {
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
        }
        returnToSettings = false;
        resolutionSelect = null;
        overlay = Child("Modal overlay", canvas, 0, 0, 0, 0);
        GameUi.Fill(overlay, Vector2.zero, Vector2.zero);
        GameUi.Picture(overlay, inGame ? Sprites.ScrimBlack : Sprites.ScrimBlue, raycast: true);
        card = Child(panelName, overlay, 0, y, size.x, size.y);
        GameUi.Picture(card, Sprites.Card, raycast: true);
        scrimFade = overlay.gameObject.AddComponent<CanvasGroup>();
        scrimFade.alpha = 0f;
        if (yellowHeader)
        {
            GameUi.Picture(Child("Header", card, 0, size.y * .5f - 59f, size.x - 8f, 110f), Sprites.Header);
        }
    }

    private void Close()
    {
        if (activeModal != this)
            return;
        activeModal = null;
        if (overlay != null)
        {
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
            overlay = null;
        }
        if (background != null)
        {
            background.interactable = backgroundWasInteractable;
            background.blocksRaycasts = backgroundBlockedRaycasts;
        }
        if (gear != null) gear.interactable = true;
        if (previousSelection != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        scrimFade = null;
        PlayerPrefs.Save();
    }

    private void BuildGear()
    {
        EnsureCanvas();
        var rect = GameUi.Child("Settings gear", canvas, Vector2.one, Vector2.one, new Vector2(-28, -23), new Vector2(82, 82));
        gear = GameUi.Button(rect, Sprites.ButtonSky);
        GameUi.Picture(Child("Gear icon", rect, 0, 3, 45, 45), Sprites.Gear);
        gear.onClick.AddListener(ShowSettings);
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;
        var go = new GameObject("Game menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        canvas = (RectTransform)go.transform;
        var menuCanvas = go.GetComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 70;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        if (EventSystem.current == null)
        {
            var input = new GameObject("Menu EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(input, gameObject.scene);
        }
    }

    private void Section(string title, float y, float height)
    {
        float width = card.rect.width - (inGame ? 75f : 68f);
        GameUi.Picture(Child(title + " section", card, 0, y, width, height), Sprites.Section);
        float x = -width * .5f + (inGame ? 89f : 79f);
        var chip = Child(title + " tab", card, x, y + height * .5f, inGame ? 141f : 134f, 57);
        GameUi.Picture(chip, Sprites.FieldBlue);
        GameUi.Label(Child("Label", chip, 0, 0, 108, 46), title, 36, TextAlignmentOptions.Center);
    }

    private void VolumeRow(string label, string key, float y)
    {
        TextAt(key + " label", label, inGame ? -154 : -137, y, 230, 50, 35, TextAlignmentOptions.MidlineLeft);
        float width = inGame ? 359f : 336f;
        var row = Child(key + " slider", card, inGame ? 88 : 83, y, width, 49);
        GameUi.Picture(Child("Track", row, 0, 0, width, 28), Sprites.SliderTrack, raycast: true);
        var fillArea = Child("Fill area", row, 0, 0, width - 24, 20);
        var fill = Child("Fill", fillArea, 0, 0, 0, 0);
        GameUi.Fill(fill, new Vector2(-8, 0), Vector2.zero);
        GameUi.Picture(fill, Sprites.SliderFill);
        var handleArea = Child("Handle area", row, 0, 0, width - 24, 48);
        var handle = Child("Handle", handleArea, 0, 0, 46, -2);
        var target = GameUi.Picture(handle, Sprites.Knob, raycast: true);
        var slider = row.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        GameUi.StyleSelectable(slider, target);
        slider.SetValueWithoutNotify(Volume(key));
        slider.onValueChanged.AddListener(value =>
        {
            PlayerPrefs.SetFloat(PreferencePrefix + key, value);
            if (key == "Master") AudioListener.volume = value;
            else SetMixer(key, value);
            if (key == "Sfx") SetMixer("Ui", value);
        });
    }

    private void DisplayRows()
    {
        float labelX = inGame ? -154 : -134;
        float toggleY = inGame ? -123 : -109;
        float resolutionY = inGame ? -200 : -181;
        float controlRight = inGame ? 268.5f : 256f;
        float toggleWidth = inGame ? 123f : 112f;
        const float toggleHeight = 54f;
        const float knobSize = 50f;
        TextAt("Fullscreen label", "전체 화면", labelX, toggleY, 230, 50, 35, TextAlignmentOptions.MidlineLeft);
        var toggle = ButtonAt("Fullscreen", controlRight - toggleWidth * .5f, toggleY,
            toggleWidth, toggleHeight, Sprites.ToggleOff, "", null);
        var toggleImage = (Image)toggle.targetGraphic;
        var knob = Child("Knob", toggle.transform, 0, 0, knobSize, knobSize);
        GameUi.Picture(knob, Sprites.Knob);
        float knobOffset = (toggleWidth - toggleHeight) * .5f;
        void Redraw(bool full)
        {
            toggleImage.sprite = full ? Sprites.ToggleOn : Sprites.ToggleOff;
            knob.anchoredPosition = new Vector2(full ? knobOffset : -knobOffset, 0);
        }
        bool fullscreen = PlayerPrefs.GetInt(PreferencePrefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        Redraw(fullscreen);
        toggle.onClick.AddListener(() =>
        {
            fullscreen = !fullscreen;
            PlayerPrefs.SetInt(PreferencePrefix + "Fullscreen", fullscreen ? 1 : 0);
            ApplyDisplay();
            Redraw(fullscreen);
        });

        TextAt("Resolution label", "해상도", labelX, resolutionY, 230, 50, 35, TextAlignmentOptions.MidlineLeft);
        int width = PlayerPrefs.GetInt(PreferencePrefix + "ScreenWidth", Screen.width);
        int height = PlayerPrefs.GetInt(PreferencePrefix + "ScreenHeight", Screen.height);
        var current = new Vector2Int(width, height);
        var sizes = new List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!sizes.Contains(size)) sizes.Add(size);
        }
        if (!sizes.Contains(current)) sizes.Add(current);
        resolutionSelect = CreateResolutionDropdown(controlRight, resolutionY);
        resolutionSelect.AddOptions(sizes.ConvertAll(size => $"{size.x} × {size.y}"));
        resolutionSelect.SetValueWithoutNotify(sizes.IndexOf(current));
        resolutionSelect.onValueChanged.AddListener(index =>
        {
            PlayerPrefs.SetInt(PreferencePrefix + "ScreenWidth", sizes[index].x);
            PlayerPrefs.SetInt(PreferencePrefix + "ScreenHeight", sizes[index].y);
            ApplyDisplay();
        });
    }

    private TMP_Dropdown CreateResolutionDropdown(float right, float y)
    {
        float width = inGame ? 357 : 326;
        var rect = Child("Resolution", card, right - width * .5f, y, width, 58);
        var face = GameUi.Picture(rect, Sprites.FieldBlue, raycast: true);
        var drop = rect.gameObject.AddComponent<TMP_Dropdown>();
        GameUi.StyleSelectable(drop, face);
        drop.captionText = GameUi.Label(Child("Current size", rect, -15, 0, width - 83, 50), "", 35, TextAlignmentOptions.MidlineLeft);
        GameUi.Picture(Child("Caret", rect, width * .5f - 34, 0, 21, 21), Sprites.Caret, preserveAspect: true);
        var template = GameUi.Child("Template", rect, new Vector2(.5f, 0), new Vector2(.5f, 1), new Vector2(0, -3), new Vector2(width, 164));
        GameUi.Picture(template, Sprites.Dropdown, raycast: true);
        var viewport = Child("Viewport", template, 0, 0, 0, 0);
        GameUi.Fill(viewport, new Vector2(5, 5), new Vector2(-5, -5));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = GameUi.Child("Content", viewport, Vector2.up, Vector2.up, Vector2.zero, new Vector2(0, 40));
        content.anchorMax = Vector2.one;
        var item = GameUi.Child("Item", content, Vector2.up, Vector2.up, Vector2.zero, new Vector2(0, 40));
        item.anchorMax = Vector2.one;
        var itemFace = GameUi.Picture(item, Sprites.Option, raycast: true);
        var choice = item.gameObject.AddComponent<Toggle>();
        GameUi.StyleSelectable(choice, itemFace);
        var selected = Child("Selected", item, -width * .5f + 28, 0, 11, 11);
        choice.graphic = GameUi.Picture(selected, Sprites.SelectedDot);
        drop.itemText = GameUi.Label(Child("Size", item, 0, 0, width - 64, 37), "", 28, TextAlignmentOptions.Center);
        var scroll = template.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        drop.template = template;
        template.gameObject.SetActive(false);
        return drop;
    }

    private void ApplyDisplay()
    {
        int width = PlayerPrefs.GetInt(PreferencePrefix + "ScreenWidth", Screen.width);
        int height = PlayerPrefs.GetInt(PreferencePrefix + "ScreenHeight", Screen.height);
        bool fullscreen = PlayerPrefs.GetInt(PreferencePrefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    private static float Volume(string key) => Mathf.Clamp01(PlayerPrefs.GetFloat(PreferencePrefix + key, 1f));

    private void SetMixer(string key, float value)
    {
        if (mixer != null)
            mixer.SetFloat(key + "Volume", value > .0001f ? 20f * Mathf.Log10(value) : -80f);
    }

    private static RectTransform Child(string name, Transform parent, float x, float y, float w, float h)
        => GameUi.Child(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, y), new Vector2(w, h));

    private TMP_Text TextAt(string name, string text, float x, float y, float w, float h, int size, TextAlignmentOptions align)
        => GameUi.Label(Child(name, card, x, y, w, h), text, size, align);

    private Button ButtonAt(string name, float x, float y, float w, float h, Sprite sprite, string text, UnityEngine.Events.UnityAction action)
    {
        var rect = Child(name, card, x, y, w, h);
        var button = GameUi.Button(rect, sprite);
        if (!string.IsNullOrEmpty(text))
            GameUi.Label(Child("Label", rect, 0, 3, w - 24, h - 10), text, 44, TextAlignmentOptions.Center);
        if (action != null) button.onClick.AddListener(action);
        return button;
    }
}
