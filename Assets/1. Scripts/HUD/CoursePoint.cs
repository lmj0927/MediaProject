using UnityEngine;

/// <summary>
/// 코스 경로의 한 지점. CourseRoute 오브젝트의 자식으로 두고, 하이어라키 순서가 곧 경로 순서임.
/// 지점을 늘릴 때는 하나 골라서 Ctrl+D로 복제한 뒤 옮기면 됨(복제본은 바로 다음 순서로 들어감).
/// 첫 지점은 출발점, 마지막 지점은 결승점으로 처리함.
/// </summary>
public class CoursePoint : MonoBehaviour
{
    [Tooltip("켜면 진행바에 체크포인트(마름모)로 표시함. 끄면 길 모양만 잡는 지점이 됨. 출발점·결승점은 이 값과 상관없음.")]
    public bool checkpoint;

    [Tooltip("켜면 이 지점에서 곡선으로 돌지 않고 각지게 꺾임. 90도로 꺾이는 코너 같은 곳에 씀.")]
    public bool sharpCorner;

#if UNITY_EDITOR
    // 씬 뷰에서 지점을 클릭해서 고를 수 있게 지점 쪽에서 그림
    private void OnDrawGizmos()
    {
        var parent = transform.parent;
        if (parent == null || parent.GetComponent<CourseRoute>() == null) return;

        int index = transform.GetSiblingIndex();
        bool isStart = index == 0;
        bool isFinish = index == parent.childCount - 1;

        if (isStart) Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.9f);
        else if (isFinish) Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
        else if (checkpoint) Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
        else Gizmos.color = new Color(1f, 1f, 1f, 0.7f);

        float size = isStart || isFinish || checkpoint ? 1.4f : 0.7f;
        if (sharpCorner) Gizmos.DrawCube(transform.position, Vector3.one * size * 1.6f);
        else Gizmos.DrawSphere(transform.position, size);
    }
#endif
}
