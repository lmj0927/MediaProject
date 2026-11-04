using System.Text;
using UnityEngine;

public sealed class RoomTitleView : MonoBehaviour
{
    [SerializeField] private GameObject _form;
    [SerializeField] private GameObject _capacityRow;
    [SerializeField] private GameObject _codeRow;
    [SerializeField] private UnityEngine.UI.InputField _nickname;
    [SerializeField] private UnityEngine.UI.InputField _code;
    [SerializeField] private UnityEngine.UI.Text _formTitle;
    [SerializeField] private UnityEngine.UI.Text _capacityText;
    [SerializeField] private UnityEngine.UI.Text _status;
    [SerializeField] private UnityEngine.UI.Text _titleStatus;
    [SerializeField] private UnityEngine.UI.Text _submitText;
    [SerializeField] private UnityEngine.UI.Button _submit;
    [SerializeField] private UnityEngine.UI.Button _cancel;
    [SerializeField] private UnityEngine.UI.Button _less;
    [SerializeField] private UnityEngine.UI.Button _more;

    private int _capacity = RoomSession.MaxPlayers;
    private bool _create;
    private bool _connecting;

    private void Start()
    {
        _titleStatus.text = RoomSession.TakeReturnMessage() ?? "";
    }

    public void ShowCreate() => ShowForm(true);
    public void ShowJoin() => ShowForm(false);

    private void ShowForm(bool create)
    {
        if (_connecting)
            return;

        _create = create;
        _titleStatus.text = "";
        _status.text = "";
        _formTitle.text = create ? "방 만들기" : "방 참여하기";
        _submitText.text = create ? "방 만들기" : "참여하기";
        _capacityRow.SetActive(create);
        _codeRow.SetActive(!create);
        _form.SetActive(true);
        UpdateCapacity();
        _nickname.Select();
        _nickname.ActivateInputField();
    }

    public void HideForm() => _form.SetActive(false);
    public void AddCapacity() => SetCapacity(_capacity + 1);
    public void RemoveCapacity() => SetCapacity(_capacity - 1);

    private void SetCapacity(int capacity)
    {
        _capacity = Mathf.Clamp(capacity, 1, RoomSession.MaxPlayers);
        UpdateCapacity();
    }

    private void UpdateCapacity()
    {
        _capacityText.text = $"{_capacity}명";
        _less.interactable = !_connecting && _capacity > 1;
        _more.interactable = !_connecting && _capacity < RoomSession.MaxPlayers;
    }

    public async void Submit()
    {
        if (_connecting || RoomSession.Active != null)
            return;

        string nickname = RoomSession.ReadNickname(Encoding.UTF8.GetBytes(_nickname.text.Trim()));
        string code = _code.text.Trim().ToUpperInvariant();
        if (nickname == null)
        {
            _status.text = $"닉네임을 1~{RoomSession.NicknameLength}자로 입력해 주세요.";
            return;
        }

        if (!_create && !RoomSession.IsValidCode(code))
        {
            _status.text = "6자리 방 코드를 확인해 주세요.";
            return;
        }

        SetConnecting(true);
        _status.text = "Photon에 연결하는 중...";
        var session = new GameObject("Room Session").AddComponent<RoomSession>();
        string error = await session.Connect(_create, code, _capacity, nickname);
        if (this != null)
        {
            SetConnecting(false);
            _status.text = error ?? "";
        }
    }

    private void SetConnecting(bool connecting)
    {
        _connecting = connecting;
        _submit.interactable = !connecting;
        _cancel.interactable = !connecting;
        _nickname.interactable = !connecting;
        _code.interactable = !connecting;
        UpdateCapacity();
    }
}
