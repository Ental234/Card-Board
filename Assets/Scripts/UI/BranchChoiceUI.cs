using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 입력 UI (v0.6) — 이름은 씬 참조를 지키려고 그대로 둔다
//
// ① 목적지 선택 — 주사위를 굴리면 그 거리에 도달 가능한 노드들을 맵 위에 강조하고 직접 클릭하게 한다.
//    화면 아래 팝업을 읽고 버튼을 누르는 방식은 흐름을 끊는다. 보드 위에서 바로 고르는 편이 조작이 짧다.
// ② 조우지점 보급 메뉴 — 선착해 기다리는 턴마다 택 1 (구매 / HP 회복(소) / 카드 강화 / 카드 제거)
// ③ 재굴림 선택 — 재굴림 카드를 쓴 턴, 주사위 결과를 보고 "이대로 / 다시 굴리기" 중 고른다
public class BranchChoiceUI : MonoBehaviour
{
    [SerializeField] private BoardPhaseManager boardPhaseManager;
    [SerializeField] private BoardMapView      mapView;

    [Header("목적지 강조")]
    [SerializeField] private Color highlightColor = new(1f, 0.85f, 0.35f, 0.9f);
    [SerializeField] private float highlightSize  = 78f;

    [Header("라벨")]
    [SerializeField] private TMP_FontAsset labelFont;
    [SerializeField] private int           labelSize   = 16;
    [SerializeField] private float         labelOffset = 44f;  // 노드에서 띄우는 거리

    private readonly List<GameObject> spawned = new();
    [System.NonSerialized] private GameObject supplyMenu;
    [System.NonSerialized] private GameObject rerollMenu;

    private void OnEnable()
    {
        if (boardPhaseManager != null)
        {
            boardPhaseManager.OnDestinationChoiceRequested += ShowDestinations;
            boardPhaseManager.OnPlayerSupplyRequested      += ShowSupplyMenu;
            boardPhaseManager.OnRerollOffered              += ShowRerollMenu;
        }
        SubscribeRest();
        if (GameManager.Instance != null && GameManager.Instance.IsRestPending && !DeckViewerUI.IsOpen) ShowRestMenu();

        // 패널이 꺼져 있는 동안 요청이 왔다면 켜지는 순간 다시 띄운다 — 안 그러면 보드가 영영 멈춘다
        if (boardPhaseManager != null)
        {
            var pending = boardPhaseManager.PendingDestinationCandidates;
            if (pending != null) ShowDestinations(new List<int>(pending));

            var supply = boardPhaseManager.PendingSupplyRequest;
            if (supply != null) ShowSupplyMenu(supply);

            int reroll = boardPhaseManager.PendingRerollValue;
            if (reroll > 0) ShowRerollMenu(reroll);
        }
    }

    private void OnDisable()
    {
        if (boardPhaseManager != null)
        {
            boardPhaseManager.OnDestinationChoiceRequested -= ShowDestinations;
            boardPhaseManager.OnPlayerSupplyRequested      -= ShowSupplyMenu;
            boardPhaseManager.OnRerollOffered              -= ShowRerollMenu;
        }
        UnsubscribeRest();
        CloseRestMenu();

        Clear();
        CloseSupplyMenu();
        CloseRerollMenu();
    }

    // ── ① 목적지 선택 ───────────────────────────────────

    private void ShowDestinations(List<int> candidates)
    {
        Clear();

        // 강조를 그릴 수 없으면 보드가 선택을 기다리며 영영 멈춘다.
        if (mapView == null || !mapView.HasLayout)
        {
            Debug.LogWarning("[BranchChoiceUI] 맵 레이아웃이 없어 목적지를 표시할 수 없습니다. 첫 후보로 진행합니다.");
            boardPhaseManager.SubmitDestination(candidates[0]);
            return;
        }

        foreach (int target in candidates)
            BuildHighlight(target);
    }

    private void BuildHighlight(int target)
    {
        Vector2 pos = mapView.GetNodePosition(target);

        // 노드 뒤에 깔리는 강조 사각형 — 이게 클릭 대상이다
        var go = new GameObject($"Destination_{target}", typeof(RectTransform));
        go.transform.SetParent(mapView.MapRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = new Vector2(highlightSize, highlightSize);

        var img = go.AddComponent<Image>();
        img.color = highlightColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => ChooseDestination(target));

        // 노드 위로 올려 클릭을 받게 하되, 노드 라벨이 보이도록 반투명 테두리처럼 쓴다
        go.transform.SetAsLastSibling();
        img.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, 0.45f);
        spawned.Add(go);

        BuildLabel(pos, target);
    }

    private void BuildLabel(Vector2 nodePos, int target)
    {
        var node = boardPhaseManager.GetNode(target);
        if (node == null) return;

        var go = new GameObject($"Label_{target}", typeof(RectTransform));
        go.transform.SetParent(mapView.MapRoot, false);

        // 세로 맵 — 내 트랙은 왼쪽이라 라벨을 노드 왼쪽 바깥에 단다. 조우지점은 가운데 축이라 위에 단다
        bool shared = node.track == BoardTrack.Shared;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = shared ? new Vector2(0.5f, 0f) : new Vector2(1f, 0.5f);
        rt.anchoredPosition = nodePos + (shared ? new Vector2(0f, labelOffset) : new Vector2(-labelOffset, 0f));
        rt.sizeDelta        = new Vector2(120, 24);

        var tmp = CreateTmp(go, NodeLabel(node), labelSize);
        tmp.color     = highlightColor;
        tmp.alignment = shared ? TextAlignmentOptions.Center : TextAlignmentOptions.Right;

        spawned.Add(go);
    }

    private void ChooseDestination(int nodeIndex)
    {
        Clear();
        boardPhaseManager.SubmitDestination(nodeIndex);
    }

    private void Clear()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    // ── ② 보급 메뉴 ─────────────────────────────────────

    private void ShowSupplyMenu(NodeData encounter)
    {
        CloseSupplyMenu();

        var parent = mapView != null ? mapView.PanelRoot : (RectTransform)transform;

        supplyMenu = new GameObject("SupplyMenu", typeof(RectTransform));
        supplyMenu.transform.SetParent(parent, false);
        supplyMenu.transform.SetAsLastSibling();

        var rt = supplyMenu.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(460f, 600f);

        var bg = supplyMenu.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var layout = supplyMenu.AddComponent<VerticalLayoutGroup>();
        layout.padding              = new RectOffset(20, 20, 16, 16);
        layout.spacing              = 8f;
        layout.childAlignment       = TextAnchor.UpperCenter;
        layout.childControlWidth    = true;
        layout.childControlHeight   = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;

        bool isSupply = encounter.encounterKind == EncounterKind.Supply;
        var  title    = BoardMapView.EncounterName(encounter.encounterKind);
        AddText($"{title} - 보급 1회", 22);

        AddButton(isSupply ? "구매 (상점 열기)" : "구매 - 보급소에서만", isSupply, SupplyChoice.Shop);
        AddButton("HP 회복 (소)",                    true,  SupplyChoice.Heal);
        AddButton("강화 (카드·동료 1회)",             true,  SupplyChoice.Upgrade);
        AddButton("카드 제거 (1장, 무료)",            CanRemoveCard(), SupplyChoice.RemoveCard);
        AddButton("전열 정비 (다음 조우 1턴 방어막)",   true,  SupplyChoice.Fortify);
        AddButton("기습 준비 (다음 조우 1턴 에너지 +1)", true,  SupplyChoice.Ambush);
        AddButton("함정 설치 (다음 조우 하수인 기절)",   true,  SupplyChoice.Trap);
        AddButton("동료 훈련 (동료 1명 강화)",         HasCompanions(), SupplyChoice.TrainCompanion);
        AddButton("넘기기",                          true,  SupplyChoice.Skip);
    }

    private static bool CanRemoveCard()
        => GameManager.Instance != null && GameManager.Instance.Player.DeckManager.CanRemoveCard;

    private static bool HasCompanions()
        => GameManager.Instance != null && GameManager.Instance.Companions.Count > 0;

    // ── ④ 휴식 노드 — HP 회복 / 강화 ─────────────────────

    [System.NonSerialized] private GameObject restMenu;
    [System.NonSerialized] private bool       restSubscribed;

    private void SubscribeRest()
    {
        if (restSubscribed || GameManager.Instance == null) return;
        GameManager.Instance.OnRestRequested += ShowRestMenu;
        restSubscribed = true;
    }

    private void UnsubscribeRest()
    {
        if (!restSubscribed || GameManager.Instance == null) return;
        GameManager.Instance.OnRestRequested -= ShowRestMenu;
        restSubscribed = false;
    }

    private void ShowRestMenu()
    {
        CloseRestMenu();
        var gm = GameManager.Instance;
        if (gm == null) return;

        var parent = mapView != null ? mapView.PanelRoot : (RectTransform)transform;
        restMenu = BuildMenuFrame(parent, "RestMenu", new Vector2(380f, 200f));

        AddText(restMenu, "휴식 - 하나를 고른다", 22);
        AddMenuButton(restMenu, $"HP 회복 (+{gm.RestHealAmount})", true, () => { CloseRestMenu(); gm.RestHeal(); });
        AddMenuButton(restMenu, "강화 (카드·동료 1회)", true, () =>
        {
            CloseRestMenu();
            gm.RestUpgrade(onCancel: ShowRestMenu);   // 취소하면 이 창으로 돌아온다
        });
    }

    private void CloseRestMenu()
    {
        if (restMenu != null) Destroy(restMenu);
        restMenu = null;
    }

    // ── ③ 재굴림 선택 ───────────────────────────────────

    private void ShowRerollMenu(int value)
    {
        CloseRerollMenu();

        // 보급 메뉴와 같은 틀을 빌려 쓴다 — 버튼 두 개짜리 작은 창
        var parent = mapView != null ? mapView.PanelRoot : (RectTransform)transform;
        rerollMenu = BuildMenuFrame(parent, "RerollMenu", new Vector2(360f, 190f));

        AddText(rerollMenu, $"주사위 {value}", 22);
        AddMenuButton(rerollMenu, $"{value}칸으로 간다", true, () => ChooseReroll(false));
        AddMenuButton(rerollMenu, "다시 굴린다 (새 값을 그대로 쓴다)", true, () => ChooseReroll(true));
    }

    // 선택이 버튼 말고 다른 경로로 들어와도(테스트·향후 단축키) 대기가 끝나면 창이 남지 않게 한다
    private void Update()
    {
        if (rerollMenu != null && boardPhaseManager != null && boardPhaseManager.PendingRerollValue == 0)
            CloseRerollMenu();
    }

    private void ChooseReroll(bool reroll)
    {
        CloseRerollMenu();
        boardPhaseManager.SubmitReroll(reroll);
    }

    private void CloseRerollMenu()
    {
        if (rerollMenu != null) Destroy(rerollMenu);
        rerollMenu = null;
    }

    // 세로로 쌓이는 작은 선택 창 틀
    private GameObject BuildMenuFrame(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.SetAsLastSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(20, 20, 16, 16);
        layout.spacing                = 8f;
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        return go;
    }

    private void AddText(string text, int size) => AddText(supplyMenu, text, size);

    private void AddText(GameObject menu, string text, int size)
    {
        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(menu.transform, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = size * 1.8f;
        CreateTmp(go, text, size).color = new Color(1f, 0.9f, 0.6f);
    }

    private void AddButton(string label, bool interactable, SupplyChoice choice)
        => AddMenuButton(supplyMenu, label, interactable, () => ChooseSupply(choice), $"Supply_{choice}");

    private void AddMenuButton(GameObject menu, string label, bool interactable, System.Action onClick,
                               string name = "MenuButton")
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(menu.transform, false);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 44f;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.30f, 0.25f, 0.18f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable  = interactable;
        btn.onClick.AddListener(() => onClick());

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var tmp = CreateTmp(textGo, label, 20);
        tmp.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    private void ChooseSupply(SupplyChoice choice)
    {
        CloseSupplyMenu();
        boardPhaseManager.SubmitSupplyChoice(choice);
    }

    private void CloseSupplyMenu()
    {
        if (supplyMenu != null) Destroy(supplyMenu);
        supplyMenu = null;
    }

    // ── 유틸 ────────────────────────────────────────────

    private TextMeshProUGUI CreateTmp(GameObject go, string text, int size)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.fontStyle        = FontStyles.Normal;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.raycastTarget    = false;
        tmp.enableAutoSizing = false;
        if (labelFont != null) { tmp.font = labelFont; tmp.fontSharedMaterial = labelFont.material; }
        return tmp;
    }

    private static string NodeLabel(NodeData node) => node.type switch
    {
        NodeType.Monster   => "몬스터",
        NodeType.Elite     => "정예",
        NodeType.Event     => "이벤트",
        NodeType.Shop      => "상점",
        NodeType.Rest      => "휴식",
        NodeType.Treasure  => "보물",
        NodeType.Curse     => "저주",
        NodeType.Start     => "시작",
        NodeType.Encounter => BoardMapView.EncounterName(node.encounterKind),
        _                  => "빈 칸",
    };
}
