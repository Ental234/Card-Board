using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 그래프 시각화 (v0.6 — 스테이지 전체를 세로로)
//
// 배치 전략: 스테이지 전체를 **아래에서 위로** 한 번에 펼친다.
//   맨 아래 = 시작 칸, 구획마다 위로 쌓이고 맨 위 = 최종 결전
//   조우지점은 가운데 세로축, 왼쪽 = 플레이어 트랙, 오른쪽 = 보스 트랙
//
// **노드의 높이 = 그 구획 조우지점까지 남은 칸 수.** 두 트랙이 같은 눈금을 쓰므로
// 내 말과 보스 말의 높이 차이가 곧 남은 칸 수 차이가 된다 — 숫자를 띄우지 않아도 경주가 읽힌다.
// 짧은 갈래는 시작점에서 긴 선으로 이어져 "지름길"이라는 것이 모양으로 드러난다.
// x는 갈래(lane)에서 나온다.
// 아직 열리지 않은 구획은 어둡게 가린다. 구획이 열리면 서서히 드러난다 (나중에 안개 연출로 교체).
// 한 화면에 다 안 들어가므로 맵 루트를 위아래로 움직여 말을 따라간다 (휠로 직접 훑어볼 수도 있다).
// 격자에 맞추지 않는다 — 좌표 규칙은 GetNodePosition 하나로만 외부에 노출한다.
public class BoardMapView : MonoBehaviour
{
    [SerializeField] private BoardPhaseManager boardPhaseManager;

    [Header("노드")]
    [SerializeField] private GameObject    nodePrefab;
    [SerializeField] private RectTransform mapRoot;

    // 필드 이름을 가로 배치 때와 다르게 둔다 — 같은 이름이면 씬에 저장된 옛 값이 코드 기본값을 덮는다
    [Header("세로 배치")]
    [SerializeField] private float rowStep        = 52f;    // 진행 한 칸의 세로 간격
    [SerializeField] private float trackOffsetX   = 110f;   // 가운데 축에서 첫 갈래까지
    [SerializeField] private float laneGapX       = 120f;   // 갈래 사이 가로 간격
    [SerializeField] private float nodeScale      = 0.75f;
    [SerializeField] private float encounterScale = 1.5f;

    [Header("스크롤")]
    [SerializeField] private float focusBias    = -120f;  // 따라가는 말을 화면 중앙보다 얼마나 아래에 둘지
    [SerializeField] private float followSpeed  = 6f;
    [SerializeField] private float wheelSpeed   = 90f;
    [SerializeField] private float viewportTopInset = 100f;   // HUD(골드·스테이지 표시)를 가리지 않도록 위를 비운다

    [Header("간선")]
    [SerializeField] private Color edgeColor     = new(0.45f, 0.42f, 0.38f);
    [SerializeField] private float edgeThickness = 3f;

    [Header("아이콘")]
    [SerializeField] private RectTransform playerIcon;
    [SerializeField] private RectTransform bossIcon;

    [Header("노드 타입 색상")]
    [SerializeField] private Color monsterColor   = new(0.80f, 0.30f, 0.30f);
    [SerializeField] private Color eliteColor     = new(0.80f, 0.20f, 0.20f);
    [SerializeField] private Color eventColor     = new(0.40f, 0.60f, 0.80f);
    [SerializeField] private Color shopColor      = new(0.80f, 0.75f, 0.30f);
    [SerializeField] private Color restColor      = new(0.30f, 0.75f, 0.40f);
    [SerializeField] private Color treasureColor  = new(0.85f, 0.65f, 0.20f);
    [SerializeField] private Color curseColor     = new(0.50f, 0.20f, 0.60f);
    [SerializeField] private Color encounterColor = new(0.95f, 0.95f, 0.95f);
    [SerializeField] private Color emptyColor     = new(0.50f, 0.50f, 0.50f);
    [SerializeField] private Color startColor     = new(0.30f, 0.90f, 0.90f);

    [Header("트랙 구분")]
    [Range(0f, 1f)]
    [SerializeField] private float bossTrackDim = 0.6f;   // 보스 트랙 노드를 어둡게 — 내가 밟을 수 없는 길
    [Range(0f, 1f)]
    [SerializeField] private float visitedDim   = 0.45f;  // 지나온 노드·지나간 구획

    [Header("열리지 않은 구획")]
    [SerializeField] private Color hiddenColor = new(0.10f, 0.10f, 0.12f);
    [SerializeField] private float revealTime  = 0.9f;

    // ── 런타임 상태 ─────────────────────────────────────

    private class NodeView
    {
        public NodeData        node;
        public Image           image;
        public TextMeshProUGUI label;
    }

    private class EdgeView
    {
        public Image image;
        public int   section;   // 이 구획이 열려야 보인다
    }

    private readonly List<GameObject>         spawned   = new();   // 노드·간선 전부
    private readonly Dictionary<int, Vector2> positions = new();   // 노드 인덱스 → 맵 루트 기준 좌표
    private readonly List<NodeView>           nodeViews = new();
    private readonly List<EdgeView>           edgeViews = new();

    [System.NonSerialized] private float[] sectionReveal;   // 구획별 드러난 정도 0~1
    [System.NonSerialized] private int     builtVersion = -1;

    private Vector2 rootBasePos;
    private bool    rootBaseCaptured;
    [System.NonSerialized] private RectTransform panelRoot;   // 보드 패널 — 상태 텍스트·보급 메뉴가 붙는 곳
    private float   scrollY;          // 지금 화면 중앙에 오는 맵 좌표 y
    private float   scrollTargetY;
    private float   maxFocusY;

    // 보스 정보 패널 (런타임 생성 — 직렬화되면 가짜 참조가 남으므로 NonSerialized)
    // 보스 HP·거리·주사위·하수인·렐릭은 전부 패널이 보여준다. 맵 위에 따로 텍스트를 띄우지 않는다.
    [System.NonSerialized] private BossInfoPanelUI bossPanel;

    private void OnEnable()
    {
        boardPhaseManager.OnPositionsChanged  += OnPositionsChanged;
        boardPhaseManager.OnSectionChanged    += OnSectionChanged;
    }

    private void OnDisable()
    {
        boardPhaseManager.OnPositionsChanged  -= OnPositionsChanged;
        boardPhaseManager.OnSectionChanged    -= OnSectionChanged;
    }

    private void Update()
    {
        if (positions.Count == 0) return;

        AnimateReveal();
        UpdateScroll();
    }

    private void OnPositionsChanged()
    {
        if (builtVersion != boardPhaseManager.BoardVersion) { BuildMap(); return; }

        RefreshNodeTint();
        RefreshIcons();
        FocusOn(boardPhaseManager.PlayerPosition);
    }

    // 구획이 열렸다 — 다시 짓지 않는다. AnimateReveal이 서서히 드러낸다 (안개 연출이 여기 붙는다)
    private void OnSectionChanged(int _)
    {
        if (builtVersion != boardPhaseManager.BoardVersion) return;   // 새 스테이지는 BuildMap이 처리
        FocusOn(boardPhaseManager.PlayerPosition);
    }

    // ── 맵 생성 ─────────────────────────────────────────
    //
    // 보드 페이즈로 돌아올 때마다 UIManager가 부른다. 같은 보드면 다시 짓지 않는다 —
    // 다시 지으면 드러내기 연출이 되풀이되고 스크롤 위치가 튄다.

    public void BuildMap()
    {
        CaptureRootBase();
        EnsureLegendButton();
        EnsureDeckButton();
        EnsureBossPanel();

        if (boardPhaseManager.TotalNodes == 0) return;

        if (builtVersion == boardPhaseManager.BoardVersion && positions.Count > 0)
        {
            RefreshNodeTint();
            RefreshIcons();
            FocusOn(boardPhaseManager.PlayerPosition, instant: true);
            return;
        }

        Clear();
        builtVersion = boardPhaseManager.BoardVersion;

        Layout();
        DrawEdges();
        DrawNodes();
        AttachIcons();

        // 새 스테이지 — 지금 구획까지만 열린 상태로 시작한다
        sectionReveal = new float[Mathf.Max(1, boardPhaseManager.SectionCount)];
        for (int s = 0; s < sectionReveal.Length; s++)
            sectionReveal[s] = s <= boardPhaseManager.CurrentSection ? 1f : 0f;

        RefreshNodeTint();
        RefreshIcons();
        FocusOn(boardPhaseManager.PlayerPosition, instant: true);
    }

    // 맵이 한 화면보다 길어 스크롤하므로, 잘라낼 뷰포트(RectMask2D) 안으로 맵 루트를 옮긴다.
    // 씬을 건드리지 않고 런타임에 만든다 (프리팹·씬 오버라이드 함정 회피).
    private void CaptureRootBase()
    {
        if (rootBaseCaptured || mapRoot == null) return;

        panelRoot = mapRoot.parent as RectTransform;

        var viewportGo = new GameObject("MapViewport", typeof(RectTransform));
        var viewport   = viewportGo.GetComponent<RectTransform>();
        viewport.SetParent(panelRoot, false);
        viewport.SetSiblingIndex(mapRoot.GetSiblingIndex());
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(0f, -viewportTopInset);
        viewportGo.AddComponent<RectMask2D>();

        mapRoot.SetParent(viewport, worldPositionStays: true);

        rootBasePos      = mapRoot.anchoredPosition;
        rootBaseCaptured = true;
    }

    // 맵 루트는 뷰포트 안으로 옮겨지므로, 화면에 고정돼야 하는 UI는 이 패널에 붙인다
    public RectTransform PanelRoot => panelRoot != null ? panelRoot : mapRoot.parent as RectTransform;

    // ── 배치 ────────────────────────────────────────────

    private void Layout()
    {
        positions.Clear();

        int sections = Mathf.Max(1, boardPhaseManager.SectionCount);

        // 노드마다 "이 구획 조우지점까지 남은 칸 수"를 재고, 구획 높이는 그 최대값으로 정한다
        var remaining = new Dictionary<int, int>();
        var deepest   = new int[sections];

        for (int i = 0; i < boardPhaseManager.TotalNodes; i++)
        {
            var n = boardPhaseManager.GetNode(i);
            if (n == null || n.track == BoardTrack.Shared) continue;

            int steps = boardPhaseManager.StepsToEncounterOf(i, n.section, n.track);
            if (steps < 0) steps = 1;                   // 도달 불가 (정상 보드에선 없음)
            remaining[i] = steps;
            deepest[n.section] = Mathf.Max(deepest[n.section], steps);
        }

        // baseY[s] = 구획 s의 시작 높이, baseY[s+1] = 그 구획 조우지점 높이
        var baseY = new float[sections + 1];
        for (int s = 0; s < sections; s++)
            baseY[s + 1] = baseY[s] + rowStep * (deepest[s] + 1);

        for (int i = 0; i < boardPhaseManager.TotalNodes; i++)
        {
            var n = boardPhaseManager.GetNode(i);
            if (n == null) continue;

            if (n.type == NodeType.Start)
            {
                positions[i] = Vector2.zero;
            }
            else if (n.IsEncounter)
            {
                positions[i] = new Vector2(0f, baseY[n.section + 1]);
            }
            else
            {
                // 조우지점에서 남은 칸 수만큼 아래로 — 같은 높이면 남은 거리가 같다
                float y    = baseY[n.section + 1] - remaining[i] * rowStep;
                float dist = trackOffsetX + n.lane * laneGapX;
                positions[i] = new Vector2(n.track == BoardTrack.Player ? -dist : dist, y);
            }
        }

        maxFocusY = baseY[sections];
    }

    // ── 그리기 ──────────────────────────────────────────

    private void DrawEdges()
    {
        foreach (var kv in positions)
        {
            var node = boardPhaseManager.GetNode(kv.Key);
            if (node == null) continue;

            foreach (int next in node.exits)
            {
                if (!positions.TryGetValue(next, out var to)) continue;

                // 간선은 도착 노드의 구획을 따른다 — 조우지점에서 다음 구획으로 나가는 간선도 다음 구획과 함께 열린다
                var target = boardPhaseManager.GetNode(next);
                edgeViews.Add(new EdgeView { image = DrawEdge(kv.Value, to), section = target.section });
            }
        }
    }

    // UGUI에는 선 요소가 없으므로 얇은 Image를 회전·신축해 선분으로 쓴다
    private Image DrawEdge(Vector2 from, Vector2 to)
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
        return img;
    }

    private void DrawNodes()
    {
        foreach (var kv in positions)
        {
            var node = boardPhaseManager.GetNode(kv.Key);
            if (node == null) continue;

            var go = Instantiate(nodePrefab, mapRoot);
            go.name = $"Node_{node.index}";

            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = kv.Value;
            rt.localScale = Vector3.one * (node.track == BoardTrack.Shared ? encounterScale : nodeScale);

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = GetNodeLabel(node);

            nodeViews.Add(new NodeView
            {
                node  = node,
                image = go.GetComponentInChildren<Image>(),
                label = label,
            });

            spawned.Add(go);
        }
    }

    // 아이콘은 맵과 함께 스크롤돼야 하므로 맵 루트 아래로 옮긴다
    private void AttachIcons()
    {
        foreach (var icon in new[] { playerIcon, bossIcon })
        {
            if (icon == null) continue;
            if (icon.parent != mapRoot) icon.SetParent(mapRoot, false);
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.SetAsLastSibling();
        }
    }

    // ── 구획 드러내기 ───────────────────────────────────

    private void AnimateReveal()
    {
        if (sectionReveal == null) return;

        bool  changed = false;
        int   current = boardPhaseManager.CurrentSection;
        float step    = revealTime > 0f ? Time.deltaTime / revealTime : 1f;

        for (int s = 0; s < sectionReveal.Length; s++)
        {
            float target = s <= current ? 1f : 0f;
            if (Mathf.Approximately(sectionReveal[s], target)) continue;
            sectionReveal[s] = Mathf.MoveTowards(sectionReveal[s], target, step);
            changed = true;
        }

        if (changed) RefreshNodeTint();
    }

    private float RevealOf(int section)
        => sectionReveal == null || section < 0 || section >= sectionReveal.Length ? 1f : sectionReveal[section];

    private void RefreshNodeTint()
    {
        int current = boardPhaseManager.CurrentSection;

        foreach (var v in nodeViews)
        {
            if (v.image == null) continue;
            var node = v.node;

            // 시작 칸은 늘 보인다. 조우지점은 자기가 끝내는 구획과 함께 드러난다
            float reveal = node.type == NodeType.Start ? 1f : RevealOf(node.section);

            Color c = GetNodeColor(node);
            if (node.track == BoardTrack.Boss) c = Dim(c, bossTrackDim);

            bool isHere  = node.index == boardPhaseManager.PlayerPosition
                        || node.index == boardPhaseManager.BossPosition;
            bool visited = (node.track == BoardTrack.Player && node.claimedByPlayer)
                        || (node.track == BoardTrack.Boss   && node.claimedByBoss);
            bool passed  = node.section < current && node.track != BoardTrack.Shared;   // 되돌아갈 수 없는 구획
            if ((visited || passed) && !isHere) c = Dim(c, visitedDim);

            v.image.color = Color.Lerp(hiddenColor, c, reveal);
            if (v.label != null) v.label.alpha = Mathf.InverseLerp(0.5f, 1f, reveal);
        }

        foreach (var e in edgeViews)
        {
            if (e.image == null) continue;
            var c = edgeColor;
            c.a = Mathf.Lerp(0.12f, 1f, RevealOf(e.section));
            e.image.color = c;
        }
    }

    private static Color Dim(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);

    private void Clear()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
        nodeViews.Clear();
        edgeViews.Clear();
        positions.Clear();
    }

    // ── 스크롤 ──────────────────────────────────────────

    private void FocusOn(int nodeIndex, bool instant = false)
    {
        if (!positions.TryGetValue(nodeIndex, out var p)) return;

        scrollTargetY = Mathf.Clamp(p.y, 0f, maxFocusY);
        if (instant) { scrollY = scrollTargetY; ApplyScroll(); }
    }

    private void UpdateScroll()
    {
        // 휠로 위아래를 훑어볼 수 있다 — 말이 다시 움직이면 말을 따라간다
        float wheel = Input.mouseScrollDelta.y;
        if (!Mathf.Approximately(wheel, 0f))
            scrollTargetY = Mathf.Clamp(scrollTargetY + wheel * wheelSpeed, 0f, maxFocusY);

        if (Mathf.Approximately(scrollY, scrollTargetY)) return;

        scrollY = Mathf.Lerp(scrollY, scrollTargetY, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        if (Mathf.Abs(scrollY - scrollTargetY) < 0.5f) scrollY = scrollTargetY;
        ApplyScroll();
    }

    private void ApplyScroll()
    {
        if (mapRoot == null) return;
        mapRoot.anchoredPosition = rootBasePos + new Vector2(0f, focusBias - scrollY);
    }

    // ── 보스 정보 패널 ──────────────────────────────────

    private void EnsureBossPanel()
    {
        if (bossPanel == null && PanelRoot != null)
        {
            var label = nodePrefab != null ? nodePrefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            bossPanel = BossInfoPanelUI.Create(PanelRoot, boardPhaseManager, label != null ? label.font : null, bossIcon);
        }
        bossPanel?.RefreshNow();
    }

    // ── 외부 조회 (목적지 선택 하이라이트 배치 등) ──────

    public bool HasLayout => positions.Count > 0;

    public RectTransform MapRoot => mapRoot;

    public bool IsNodeVisible(int index) => positions.ContainsKey(index);

    public Vector2 GetNodePosition(int index)
        => positions.TryGetValue(index, out var p) ? p : Vector2.zero;

    // ── 아이콘 ──────────────────────────────────────────

    private void RefreshIcons()
    {
        int p = boardPhaseManager.PlayerPosition;
        int b = boardPhaseManager.BossPosition;

        // 같은 칸(시작 칸·조우지점)에 서면 겹쳐 보이지 않게 벌린다 — 내 트랙 쪽인 왼쪽이 나
        const float stackOffset = 22f;
        bool same = p == b;
        MoveIcon(playerIcon, p, same ? new Vector2(-stackOffset, 0f) : Vector2.zero);
        MoveIcon(bossIcon,   b, same ? new Vector2( stackOffset, 0f) : Vector2.zero);
    }

    private void MoveIcon(RectTransform icon, int nodeIndex, Vector2 offset)
    {
        if (icon == null || !positions.TryGetValue(nodeIndex, out var pos)) return;
        icon.anchoredPosition = pos + offset;
        icon.SetAsLastSibling();   // 런타임 생성 노드보다 위에
    }

    // ── 노드 색·라벨 ────────────────────────────────────

    private Color GetNodeColor(NodeData node) => node.type switch
    {
        NodeType.Monster   => monsterColor,
        NodeType.Elite     => eliteColor,
        NodeType.Event     => eventColor,
        NodeType.Shop      => shopColor,
        NodeType.Rest      => restColor,
        NodeType.Treasure  => treasureColor,
        NodeType.Curse     => curseColor,
        NodeType.Encounter => encounterColor,
        NodeType.Start     => startColor,
        _                  => emptyColor,
    };

    // Neo둥근모에 없을 수 있는 특수기호(⚔ ☠ ★ ♥) 대신 한글 한 글자를 쓴다.
    private static string GetNodeLabel(NodeData node) => node.type switch
    {
        NodeType.Monster   => "몬",
        NodeType.Elite     => "정",
        NodeType.Event     => "?",
        NodeType.Shop      => "상",
        NodeType.Rest      => "휴",
        NodeType.Treasure  => "보",
        NodeType.Curse     => "저",
        NodeType.Start     => "시",
        NodeType.Encounter => node.encounterKind switch
        {
            EncounterKind.Supply => "보급",
            EncounterKind.Camp   => "야영",
            _                    => "결전",
        },
        _                  => "·",
    };

    // ── 노드 범례 ("?" 버튼) ────────────────────────────
    //
    // 노드마다 툴팁을 달면 맵 위를 지나갈 때마다 설명이 튀어나와 시끄럽다.
    // 보드 화면 한쪽의 "?"에 호버하면 노드 종류별 핵심만 한 번에 보여준다.
    // 글자색은 맵의 노드 색과 같게 칠해 범례와 맵이 바로 대응되게 한다.

    [System.NonSerialized] private GameObject legendButton;

    // 보드의 작은 정사각 버튼 — "?"와 "덱"이 같은 모양을 쓴다
    private GameObject CreateSquareButton(string name, string label, Vector2 topLeft)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(PanelRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = topLeft;
        rt.sizeDelta        = new Vector2(48f, 48f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.30f, 0.25f, 0.18f, 0.95f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.fontSize      = label.Length > 1 ? 16 : 32;   // 16px 배수
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.color         = new Color(1f, 0.9f, 0.6f);
        var font = nodePrefab != null ? nodePrefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (font != null && font.font != null) { tmp.font = font.font; tmp.fontSharedMaterial = font.font.material; }
        return go;
    }

    // ── 덱 보기 버튼 ────────────────────────────────────

    [System.NonSerialized] private GameObject deckButton;

    private void EnsureDeckButton()
    {
        if (deckButton != null || PanelRoot == null) return;

        deckButton = CreateSquareButton("DeckButton", "덱", new Vector2(70f, -220f));   // "?" 아래
        var btn = deckButton.AddComponent<Button>();
        btn.targetGraphic = deckButton.GetComponent<Image>();
        btn.onClick.AddListener(OpenDeck);
        TooltipTrigger.Attach(deckButton, "덱 보기", "전투 덱과 보드 카드를 본다.");
    }

    private void OpenDeck()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        DeckViewerUI.Show(PanelRoot, "내 덱",
            new DeckViewerUI.Section("전투 덱", DeckViewerUI.Sorted(player.DeckManager.GetAllCards())),
            new DeckViewerUI.Section("보드 카드", DeckViewerUI.Sorted(player.BoardCardManager.GetAllCards())));
    }

    private void EnsureLegendButton()
    {
        if (legendButton != null || PanelRoot == null) return;

        legendButton = new GameObject("NodeLegendButton", typeof(RectTransform));
        legendButton.transform.SetParent(PanelRoot, false);

        var rt = legendButton.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(70f, -160f);   // 편성 버튼 아래
        rt.sizeDelta        = new Vector2(48f, 48f);

        var img = legendButton.AddComponent<Image>();
        img.color = new Color(0.30f, 0.25f, 0.18f, 0.95f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(legendButton.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text          = "?";
        tmp.fontSize      = 32;                  // 16px 배수
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.color         = new Color(1f, 0.9f, 0.6f);
        var font = nodePrefab != null ? nodePrefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (font != null && font.font != null) { tmp.font = font.font; tmp.fontSharedMaterial = font.font.material; }

        TooltipTrigger.Attach(legendButton, () => ("노드 안내", BuildLegend()), width: 420f);
    }

    private string BuildLegend()
    {
        string Line(Color c, string label, string name, string desc)
            => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{label}</color>  {name} - {desc}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Line(monsterColor,  "몬", "몬스터", "일반 전투. 카드·골드"));
        sb.AppendLine(Line(eliteColor,    "정", "정예",   "강한 전투. 렐릭"));
        sb.AppendLine(Line(eventColor,    "?",  "이벤트", "선택지 사건"));
        sb.AppendLine(Line(shopColor,     "상", "상점",   "구매. 렐릭은 방문당 1개"));
        sb.AppendLine(Line(restColor,     "휴", "휴식",   "HP 회복 또는 강화"));
        sb.AppendLine(Line(treasureColor, "보", "보물",   "전투 없이 보상"));
        sb.AppendLine();
        sb.AppendLine(Line(encounterColor, "보급", "보급소", "선착 시 상점 + 대기 턴마다 보급"));
        sb.AppendLine(Line(encounterColor, "야영", "야영지", "선착 시 HP 회복 + 대기 턴마다 보급"));
        sb.AppendLine(Line(encounterColor, "결전", "최종 결전", "턴 제한 없는 보스전"));
        sb.AppendLine("조우 전투는 2턴. 늦게 온 쪽은 전투 먼저, 보급은 나중");
        sb.AppendLine();
        sb.AppendLine("왼쪽 = 내 길 / 오른쪽 어두운 길 = 보스의 길");
        sb.Append("보스: 몬스터·정예에서 하수인, 상점에서 렐릭 획득");
        return sb.ToString();
    }

    public static string EncounterName(EncounterKind kind) => kind switch
    {
        EncounterKind.Supply => "보급소",
        EncounterKind.Camp   => "야영지",
        _                    => "최종 결전",
    };
}
