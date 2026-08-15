using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 그래프 시각화
//
// 배치 전략: 본선(메인 루프)은 원 위에 균등 배치하고,
//            우회 분기는 갈라진 지점과 합류 지점 사이를 바깥쪽으로 부풀린 호에 배치한다.
//            간선을 선분으로 그려 교차로가 눈에 보이게 한다.
public class BoardMapView : MonoBehaviour
{
    [SerializeField] private BoardPhaseManager boardPhaseManager;

    [Header("노드")]
    [SerializeField] private GameObject    nodePrefab;
    [SerializeField] private RectTransform mapRoot;
    [SerializeField] private float         mapRadius    = 300f;
    [SerializeField] private float         branchOffset = 90f;   // 분기를 바깥으로 밀어내는 거리

    [Header("간선")]
    [SerializeField] private Color edgeColor     = new(0.45f, 0.42f, 0.38f);
    [SerializeField] private float edgeThickness = 3f;

    [Header("아이콘")]
    [SerializeField] private RectTransform playerIcon;
    [SerializeField] private RectTransform bossIcon;

    [Header("노드 타입 색상")]
    [SerializeField] private Color monsterColor  = new(0.80f, 0.30f, 0.30f);
    [SerializeField] private Color eliteColor    = new(0.80f, 0.20f, 0.20f);
    [SerializeField] private Color eventColor    = new(0.40f, 0.60f, 0.80f);
    [SerializeField] private Color shopColor     = new(0.80f, 0.75f, 0.30f);
    [SerializeField] private Color restColor     = new(0.30f, 0.75f, 0.40f);
    [SerializeField] private Color treasureColor = new(0.85f, 0.65f, 0.20f);
    [SerializeField] private Color curseColor    = new(0.50f, 0.20f, 0.60f);
    [SerializeField] private Color salaryColor   = new(0.90f, 0.80f, 0.40f);
    [SerializeField] private Color emptyColor    = new(0.50f, 0.50f, 0.50f);
    [SerializeField] private Color startColor    = new(0.30f, 0.90f, 0.90f);

    private readonly List<GameObject> spawned   = new();   // 노드·간선 전부
    private Vector2[]                 positions;           // 노드 인덱스 → 화면 좌표

    private void OnEnable()
    {
        boardPhaseManager.OnDiceRolled   += OnDiceRolled;
        boardPhaseManager.OnPlayerLanded += OnPlayerLanded;
        boardPhaseManager.OnBossLanded   += OnBossLanded;
    }

    private void OnDisable()
    {
        boardPhaseManager.OnDiceRolled   -= OnDiceRolled;
        boardPhaseManager.OnPlayerLanded -= OnPlayerLanded;
        boardPhaseManager.OnBossLanded   -= OnBossLanded;
    }

    private void OnDiceRolled(int _)                => RefreshIcons();
    private void OnPlayerLanded(int _, NodeType __) => RefreshIcons();
    private void OnBossLanded(int _, NodeType __)   => RefreshIcons();

    // ── 맵 생성 (BoardPhaseManager.InitBoard 이후 호출) ─

    public void BuildMap()
    {
        Clear();

        int total = boardPhaseManager.TotalNodes;
        if (total == 0) return;

        positions = new Vector2[total];

        var mainRing = TraceMainRing(total);
        LayoutMain(mainRing);
        LayoutBranches(mainRing, total);

        DrawEdges(total);
        DrawNodes(total);

        RefreshIcons();
    }

    // ── 배치 ────────────────────────────────────────────

    // 0번에서 첫 출구만 따라가며 본선 루프 순서를 복원한다.
    private List<int> TraceMainRing(int total)
    {
        var ring    = new List<int>();
        var visited = new bool[total];
        int cur     = 0;

        while (cur >= 0 && cur < total && !visited[cur])
        {
            visited[cur] = true;
            ring.Add(cur);

            var exits = boardPhaseManager.GetNode(cur).exits;
            cur = exits.Count > 0 ? exits[0] : -1;
        }

        return ring;
    }

    private void LayoutMain(List<int> ring)
    {
        for (int i = 0; i < ring.Count; i++)
            positions[ring[i]] = PointOnCircle(AngleAt(i, ring.Count), mapRadius);
    }

    // 본선에 속하지 않은 노드 = 분기. 갈라진 곳과 합류하는 곳 사이 바깥쪽에 늘어놓는다.
    private void LayoutBranches(List<int> ring, int total)
    {
        var ringIndex = new Dictionary<int, int>();
        for (int i = 0; i < ring.Count; i++) ringIndex[ring[i]] = i;

        foreach (int split in ring)
        {
            var exits = boardPhaseManager.GetNode(split).exits;
            if (exits.Count < 2) continue;              // 교차로가 아님

            for (int e = 1; e < exits.Count; e++)       // 0번은 본선
            {
                var chain = TraceBranch(exits[e], ringIndex, out int rejoin);
                if (chain.Count == 0) continue;

                PlaceBranchChain(chain, ringIndex[split],
                                 ringIndex.TryGetValue(rejoin, out int r) ? r : ringIndex[split],
                                 ring.Count);
            }
        }
    }

    // 분기 시작 노드부터 본선에 합류할 때까지의 노드 목록
    private List<int> TraceBranch(int start, Dictionary<int, int> ringIndex, out int rejoin)
    {
        var chain = new List<int>();
        int cur   = start;
        rejoin    = start;

        // 분기 길이는 짧지만 안전을 위해 상한을 둔다
        for (int guard = 0; guard < 32; guard++)
        {
            if (ringIndex.ContainsKey(cur)) { rejoin = cur; break; }

            chain.Add(cur);
            var exits = boardPhaseManager.GetNode(cur).exits;
            if (exits.Count == 0) break;
            cur = exits[0];
        }

        return chain;
    }

    private void PlaceBranchChain(List<int> chain, int splitRing, int rejoinRing, int ringCount)
    {
        // 갈라진 지점에서 합류 지점까지의 진행 각도 (원을 따라 앞으로)
        int span = rejoinRing - splitRing;
        if (span <= 0) span += ringCount;

        for (int i = 0; i < chain.Count; i++)
        {
            float t     = (i + 1f) / (chain.Count + 1f);           // 두 지점 사이 보간
            float angle = AngleAt(splitRing + t * span, ringCount);
            positions[chain[i]] = PointOnCircle(angle, mapRadius + branchOffset);
        }
    }

    private static float AngleAt(float ringPos, int ringCount)
        => ringPos / ringCount * Mathf.PI * 2f - Mathf.PI / 2f;

    private static Vector2 PointOnCircle(float angle, float radius)
        => new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

    // ── 그리기 ──────────────────────────────────────────

    private void DrawEdges(int total)
    {
        for (int i = 0; i < total; i++)
        {
            var node = boardPhaseManager.GetNode(i);
            if (node == null) continue;

            foreach (int next in node.exits)
            {
                if (next < 0 || next >= total) continue;
                DrawEdge(positions[i], positions[next]);
            }
        }
    }

    // UGUI에는 선 요소가 없으므로 얇은 Image를 회전·신축해 선분으로 쓴다
    private void DrawEdge(Vector2 from, Vector2 to)
    {
        var go = new GameObject("Edge", typeof(RectTransform));
        go.transform.SetParent(mapRoot, false);
        go.transform.SetAsFirstSibling();      // 노드 뒤에 깔린다

        var img = go.AddComponent<Image>();
        img.color         = edgeColor;
        img.raycastTarget = false;

        var delta = to - from;
        var rt    = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = from;
        rt.sizeDelta        = new Vector2(delta.magnitude, edgeThickness);
        rt.localRotation    = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        spawned.Add(go);
    }

    private void DrawNodes(int total)
    {
        for (int i = 0; i < total; i++)
        {
            var node = boardPhaseManager.GetNode(i);
            if (node == null) continue;

            var go = Instantiate(nodePrefab, mapRoot);
            go.GetComponent<RectTransform>().anchoredPosition = positions[i];

            var img = go.GetComponentInChildren<Image>();
            if (img != null) img.color = GetNodeColor(node.type);

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = GetNodeLabel(node.type);

            // 교차로는 한 단계 크게 그려 눈에 띄게 한다
            if (node.IsIntersection)
                go.GetComponent<RectTransform>().localScale = Vector3.one * 1.35f;

            spawned.Add(go);
        }
    }

    private void Clear()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    // ── 아이콘 ──────────────────────────────────────────

    // ── 외부 조회 (교차로 화살표 배치 등) ───────────────

    public bool HasLayout => positions != null;

    public RectTransform MapRoot => mapRoot;

    public Vector2 GetNodePosition(int index)
    {
        if (positions == null || index < 0 || index >= positions.Length) return Vector2.zero;
        return positions[index];
    }

    private void RefreshIcons()
    {
        if (positions == null) return;

        MoveIcon(playerIcon, boardPhaseManager.PlayerPosition);
        MoveIcon(bossIcon,   boardPhaseManager.BossPosition);
    }

    private void MoveIcon(RectTransform icon, int nodeIndex)
    {
        if (icon == null || positions == null) return;
        if (nodeIndex < 0 || nodeIndex >= positions.Length) return;

        icon.anchoredPosition = positions[nodeIndex];
    }

    // ── 노드 색·라벨 ────────────────────────────────────

    private Color GetNodeColor(NodeType type) => type switch
    {
        NodeType.Monster  => monsterColor,
        NodeType.Elite    => eliteColor,
        NodeType.Event    => eventColor,
        NodeType.Shop     => shopColor,
        NodeType.Rest     => restColor,
        NodeType.Treasure => treasureColor,
        NodeType.Curse    => curseColor,
        NodeType.Salary   => salaryColor,
        NodeType.Start    => startColor,
        _                 => emptyColor,
    };

    // Neo둥근모에 없을 수 있는 특수기호(⚔ ☠ ★ ♥) 대신 한글 한 글자를 쓴다.
    private static string GetNodeLabel(NodeType type) => type switch
    {
        NodeType.Monster  => "몬",
        NodeType.Elite    => "정",
        NodeType.Event    => "?",
        NodeType.Shop     => "상",
        NodeType.Rest     => "휴",
        NodeType.Treasure => "보",
        NodeType.Curse    => "저",
        NodeType.Salary   => "월",
        NodeType.Start    => "시",
        _                 => "·",
    };
}
