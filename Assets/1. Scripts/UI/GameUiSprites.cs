using TMPro;
using UnityEngine;

public sealed class GameUiSprites : ScriptableObject
{
    private static GameUiSprites shared;

    public static GameUiSprites Shared
    {
        get
        {
            if (shared == null) shared = Resources.Load<GameUiSprites>("Roll With Us UI");
            return shared;
        }
    }

    public TMP_FontAsset Typography;
    public Sprite ButtonGold;
    public Sprite ButtonSky;
    public Sprite ButtonCream;
    public Sprite ButtonCoral;
    public Sprite ButtonDisabled;
    public Sprite ButtonDanger;
    public Sprite Card;
    public Sprite Header;
    public Sprite Section;
    public Sprite ScrimBlack;
    public Sprite ScrimBlue;
    public Sprite Gear;
    public Sprite Close;
    public Sprite Caret;
    public Sprite SliderTrack;
    public Sprite SliderFill;
    public Sprite Knob;
    public Sprite ToggleOff;
    public Sprite ToggleOn;
    public Sprite FieldBlue;
    public Sprite Dropdown;
    public Sprite Option;
    public Sprite SelectedDot;
    public Sprite HudPill;
    public Sprite LocalPlayerPin;
    public Sprite HudPillDim;
    public Sprite TeamDot;
    public Sprite TeamRing;
    public Sprite HudTrack;
    public Sprite HudFill;
    public Sprite ProgressTrack;
    public Sprite Checkpoint;
    public Sprite CheckpointPassed;
    public Sprite ProgressDot;
    public Sprite TravelDial;
    public Sprite TravelDot;
    public Sprite TravelArrow;
    public Sprite TiltDial;
    public Sprite TiltDot;
}
