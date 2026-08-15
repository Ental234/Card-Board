using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 교차로 방향 선택 — 맵 위에 화살표를 띄우고 직접 클릭하게 한다.
//
// 교차로는 자주 밟으므로 화면 아래 팝업을 읽고 버튼을 누르는 방식은 흐름을 끊는다.
// 마리오파티처럼 갈 방향을 보드 위에서 바로 고르는 편이 조작이 짧다.
public class BranchChoiceUI : MonoBehaviour
{
    [SerializeField] private BoardPhaseManager boardPhaseManager;
    [SerializeField] private BoardMapView      mapView;

    [Header("화살표")]
    [SerializeField] private Color arrowColor      = new(1f, 0.85f, 0.35f);
    [SerializeField] private float arrowThickness  = 14f;
    [SerializeField] private float arrowLengthRate = 0.55f;  // 두 노드 사이 거리 대비 길이
    [SerializeField] private float headSize        = 24f;

    [Header("라벨")]
    [SerializeField] private TMP_FontAsset labelFont;
    [SerializeField] private int           labelSize   = 16;
    [SerializeField] private float         labelOffset = 34f;  // 목적지 노드에서 띄우는 거리

    private readonly List<GameObject> spawned = new();

    private void OnEnable()
    {
        if (boardPhaseManager != null)
            boardPhaseManager.OnBranchChoiceRequested += Show;
    }

    private void OnDisable()
    {
        if (boardPhaseManager != null)
            boardPhaseManager.OnBranchChoiceRequested -= Show;

        Clear();
    }

    // ── 표시 ────────────────────────────────────────────

    private void Show(NodeData node, List<int> exits)
    {
        Clear();

        // 화살표를 그릴 수 없으면 보드가 선택을 기다리며 영영 멈춘다.
        // 참조 누락·레이아웃 미생성 시에는 본선으로 자동 진행시킨다.
        if (mapView == null || !mapView.HasLayout)
        {
            Debug.LogWarning("[BranchChoiceUI] 맵 레이아웃이 없어 화살표를 표시할 수 없습니다. " +
                             "본선으로 자동 진행합니다. (mapView 참조 확인 필요)");
            boardPhaseManager.SubmitBranchChoice(exits[0]);
            return;
        }

        Vector2 from = mapView.GetNodePosition(node.index);

        foreach (int target in exits)
            BuildArrow(from, target);
    }

    private void BuildArrow(Vector2 from, int target)
    {
        Vector2 to    = mapView.GetNodePosition(target);
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < 0.01f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        float length = delta.magnitude * arrowLengthRate;

        // 화살표 몸통 — 이게 클릭 대상이다
        var shaft = new GameObject($"Arrow_{target}", typeof(RectTransform));
        shaft.transform.SetParent(mapView.MapRoot, false);

        var srt = shaft.GetComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot            = new Vector2(0f, 0.5f);
        srt.anchoredPosition = from;
        srt.sizeDelta        = new Vector2(length, arrowThickness);
        srt.localRotation    = Quaternion.Euler(0, 0, angle);

        var simg = shaft.AddComponent<Image>();
        simg.color = arrowColor;

        var btn = shaft.AddComponent<Button>();
        btn.targetGraphic = simg;
        btn.onClick.AddListener(() => Choose(target));
        spawned.Add(shaft);

        // 화살촉 — 정사각형을 45도 돌려 끝에 붙인다 (별도 스프라이트 불필요)
        var head = new GameObject("Head", typeof(RectTransform));
        head.transform.SetParent(shaft.transform, false);

        var hrt = head.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0.5f);
        hrt.pivot            = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta        = new Vector2(headSize, headSize);
        hrt.localRotation    = Quaternion.Euler(0, 0, 45f);

        var himg = head.AddComponent<Image>();
        himg.color         = arrowColor;
        himg.raycastTarget = false;   // 클릭은 몸통이 받는다

        // 목적지 라벨 — 회전시키면 픽셀 글자가 뭉개지므로 맵에 수평으로 붙인다
        BuildLabel(to, delta.normalized, target);
    }

    private void BuildLabel(Vector2 destPos, Vector2 dir, int target)
    {
        var node = boardPhaseManager.GetNode(target);
        if (node == null) return;

        var go = new GameObject($"Label_{target}", typeof(RectTransform));
        go.transform.SetParent(mapView.MapRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = destPos + dir * labelOffset;
        rt.sizeDelta        = new Vector2(160, 24);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = NodeLabel(node);
        tmp.fontSize      = labelSize;
        tmp.fontStyle     = FontStyles.Normal;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.color         = arrowColor;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        if (labelFont != null) { tmp.font = labelFont; tmp.fontSharedMaterial = labelFont.material; }

        spawned.Add(go);
    }

    private void Choose(int nodeIndex)
    {
        Clear();
        boardPhaseManager.SubmitBranchChoice(nodeIndex);
    }

    private void Clear()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    private static string NodeLabel(NodeData node) => node.type switch
    {
        NodeType.Monster  => "몬스터",
        NodeType.Elite    => "정예",
        NodeType.Event    => "이벤트",
        NodeType.Shop     => "상점",
        NodeType.Rest     => "휴식",
        NodeType.Treasure => "보물",
        NodeType.Curse    => "저주",
        NodeType.Salary   => "월급",
        NodeType.Start    => "시작",
        _                 => "빈 칸",
    };
}
