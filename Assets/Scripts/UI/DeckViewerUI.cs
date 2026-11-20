using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 더미 보기 창 — 화면에 하나만 있다.
//
// 덱 / 뽑을 더미 / 버린 더미 / 소모 더미를 전부 같은 창으로 보여준다. 카드 모양은 손패와 같은 프리팹을 쓴다.
// 여러 묶음(예: 전투 덱 + 보드 카드)을 제목을 달아 한 창에 나열할 수 있다.
//
// **고르기 모드**(ShowPicker) — 강화·카드 제거처럼 "덱에서 한 장(한 명) 고르기"도 같은 창으로 한다.
// 고를 수 없는 것은 흐리게, 고르면 창이 닫히고 콜백이 불린다. 닫기·빈 곳·Esc는 취소 콜백.
//
// 씬·프리팹을 건드리지 않고 처음 열 때 런타임에 만든다 (프리팹 오버라이드 함정 회피).
// 루트 캔버스 위 정렬 캔버스(4000)로 올린다 — 툴팁(5000)보다는 아래라 카드 위 툴팁이 가려지지 않는다.
public class DeckViewerUI : MonoBehaviour
{
    public readonly struct Section
    {
        public readonly string                  title;
        public readonly IReadOnlyList<CardData> cards;
        public Section(string title, IReadOnlyList<CardData> cards) { this.title = title; this.cards = cards; }
    }

    private static DeckViewerUI instance;

    // 고르기 모드 요청 — null이면 보기 전용
    public class PickRequest
    {
        public string                          title;
        public IReadOnlyList<CardData>         cards;           // 전투 카드 (null이면 카드 묶음 없음)
        public IReadOnlyList<CompanionEntity>  companions;      // 동료 (null이면 동료 묶음 없음)
        public System.Func<CardData, bool>        canPickCard      = _ => true;
        public System.Func<CompanionEntity, bool> canPickCompanion = _ => true;
        public System.Func<CardData, string>        cardTooltip;       // 호버 시 본문 (예: 강화 미리보기)
        public System.Func<CompanionEntity, string> companionTooltip;
        public System.Action<CardData>          onPickCard;
        public System.Action<CompanionEntity>   onPickCompanion;
        public System.Action                    onCancel;
    }

    private PickRequest pick;   // 지금 열린 고르기 요청

    private const float CellScale = 0.85f;   // 손패보다 조금 작게 — 한 화면에 두 줄 이상 보이게

    private CardUI          cardPrefab;
    private TMP_FontAsset   font;
    private RectTransform   content;
    private TextMeshProUGUI titleText;
    private ScrollRect      scroll;
    private Vector2         cellSize;

    // ── 외부 API ────────────────────────────────────────

    // anyUi: 루트 캔버스를 찾기 위한 아무 UI. 카드 프리팹은 씬의 손패 UI에서 빌린다 (손패와 같은 모양)
    public static void Show(RectTransform anyUi, string title, params Section[] sections)
    {
        var hand   = Object.FindFirstObjectByType<HandUI>(FindObjectsInactive.Include);
        var prefab = hand != null ? hand.CardPrefab : null;
        if (anyUi == null || prefab == null) return;

        var view = Ensure(anyUi, prefab);
        if (view == null) return;

        view.pick = null;
        view.Fill(title, sections);
        view.gameObject.SetActive(true);
        view.transform.SetAsLastSibling();
    }

    // 고르기 모드로 연다
    public static void ShowPicker(RectTransform anyUi, PickRequest request)
    {
        var hand   = Object.FindFirstObjectByType<HandUI>(FindObjectsInactive.Include);
        var prefab = hand != null ? hand.CardPrefab : null;
        if (anyUi == null || prefab == null || request == null) { request?.onCancel?.Invoke(); return; }

        var view = Ensure(anyUi, prefab);
        if (view == null) { request.onCancel?.Invoke(); return; }

        view.pick = request;
        var sections = new List<Section>();
        if (request.cards != null) sections.Add(new Section("카드", request.cards));
        view.Fill(request.title, sections.ToArray());
        if (request.companions != null) view.AddCompanionSection(request.companions);
        view.gameObject.SetActive(true);
        view.transform.SetAsLastSibling();
    }

    public static bool IsOpen => instance != null && instance.gameObject.activeSelf;

    // 사용자가 닫았다(닫기·빈 곳·Esc) — 고르기 모드였다면 취소를 알린다
    public static void Close()
    {
        if (instance == null) return;
        var cancel = instance.pick?.onCancel;
        instance.pick = null;
        instance.gameObject.SetActive(false);
        cancel?.Invoke();
    }

    // 골랐다 — 취소 콜백 없이 닫는다
    private void Finish()
    {
        pick = null;
        gameObject.SetActive(false);
    }

    // 뽑을 더미는 순서를 들키면 안 된다 — 비용·이름 순으로 정렬해서 보여준다
    public static List<CardData> Sorted(IEnumerable<CardData> cards)
    {
        var list = new List<CardData>(cards);
        list.Sort((a, b) =>
        {
            int c = a.energyCost.CompareTo(b.energyCost);
            return c != 0 ? c : string.CompareOrdinal(a.cardName, b.cardName);
        });
        return list;
    }

    // ── 생성 ────────────────────────────────────────────

    private static DeckViewerUI Ensure(RectTransform anyUi, CardUI prefab)
    {
        if (instance != null) { instance.cardPrefab = prefab; return instance; }

        var canvas = anyUi.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;

        var go = new GameObject("DeckViewer", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        var sub = go.AddComponent<Canvas>();
        sub.overrideSorting = true;
        sub.sortingOrder    = 4000;
        go.AddComponent<GraphicRaycaster>();   // 별도 캔버스라 자기 레이캐스터가 있어야 클릭을 받는다

        var view = go.AddComponent<DeckViewerUI>();
        view.cardPrefab = prefab;
        view.font       = FindFont(canvas);
        view.Build();

        instance = view;
        go.SetActive(false);
        return view;
    }

    private static TMP_FontAsset FindFont(Canvas canvas)
    {
        foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.font != null) return t.font;
        return TMP_Settings.defaultFontAsset;
    }

    private void Build()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        // 뒤를 어둡게 덮고 클릭을 막는다. 빈 곳을 누르면 닫힌다
        var dim = gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        var dimBtn = gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(Close);

        // 창
        var window = NewRect("Window", transform);
        window.anchorMin = new Vector2(0.08f, 0.08f);
        window.anchorMax = new Vector2(0.92f, 0.92f);
        window.offsetMin = window.offsetMax = Vector2.zero;
        var winImg = window.gameObject.AddComponent<Image>();
        winImg.color = new Color(0.08f, 0.07f, 0.06f, 0.97f);

        // 창 안 클릭이 뒤 배경(닫기)으로 번지지 않게 여기서 흡수한다 — 클릭 이벤트는 부모로 타고 올라간다
        var absorb = window.gameObject.AddComponent<Button>();
        absorb.transition = Selectable.Transition.None;
        var outline = window.gameObject.AddComponent<Outline>();
        outline.effectColor    = new Color(0.72f, 0.62f, 0.38f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        // 제목
        var titleRt = NewRect("Title", window);
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot     = new Vector2(0.5f, 1f);
        titleRt.offsetMin = new Vector2(24f, -64f);
        titleRt.offsetMax = new Vector2(-140f, -16f);
        titleText = AddText(titleRt.gameObject, 32, new Color(1f, 0.86f, 0.50f), TextAlignmentOptions.MidlineLeft);

        // 닫기
        var closeRt = NewRect("Close", window);
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot            = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-16f, -16f);
        closeRt.sizeDelta        = new Vector2(110f, 44f);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.30f, 0.25f, 0.18f);
        var closeBtn = closeRt.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Close);
        var closeLabel = NewRect("Label", closeRt);
        closeLabel.anchorMin = Vector2.zero; closeLabel.anchorMax = Vector2.one;
        closeLabel.offsetMin = closeLabel.offsetMax = Vector2.zero;
        AddText(closeLabel.gameObject, 16, Color.white, TextAlignmentOptions.Center).text = "닫기";

        // 스크롤 영역
        var viewport = NewRect("Viewport", window);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(24f, 20f);
        viewport.offsetMax = new Vector2(-24f, -80f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();   // 휠·드래그를 받을 그래픽
        vpImg.color = new Color(0f, 0f, 0f, 0f);

        content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing                = 12f;
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport          = viewport;
        scroll.content           = content;
        scroll.horizontal        = false;
        scroll.movementType      = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
    }

    // ── 채우기 ──────────────────────────────────────────

    private void Fill(string title, Section[] sections)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        var prefabRt = cardPrefab.GetComponent<RectTransform>();
        cellSize = prefabRt.rect.size * CellScale;

        int total = 0;
        foreach (var s in sections) total += s.cards?.Count ?? 0;
        titleText.text = pick != null ? title : $"{title} ({total}장)";

        foreach (var s in sections)
        {
            if (sections.Length > 1 || !string.IsNullOrEmpty(s.title))
            {
                var header = NewRect("Header", content);
                header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
                AddText(header.gameObject, 16, new Color(0.75f, 0.72f, 0.62f), TextAlignmentOptions.MidlineLeft)
                    .text = $"{s.title} ({s.cards?.Count ?? 0}장)";
            }

            if (s.cards == null || s.cards.Count == 0)
            {
                var empty = NewRect("Empty", content);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                AddText(empty.gameObject, 16, new Color(0.6f, 0.58f, 0.52f), TextAlignmentOptions.MidlineLeft).text = "비어 있다";
                continue;
            }

            var grid = NewRect("Grid", content);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize        = cellSize;
            g.spacing         = new Vector2(14f, 14f);
            g.constraint      = GridLayoutGroup.Constraint.Flexible;   // 창 폭만큼 한 줄에 채운다
            g.childAlignment  = TextAnchor.UpperLeft;
            // 높이는 부모 VerticalLayoutGroup이 그리드의 선호 높이로 정한다 (자식에 ContentSizeFitter를 겹치면 경고)

            foreach (var card in s.cards)
            {
                if (card == null) continue;

                // 그리드 칸 안에 카드 프리팹을 축소해 넣는다 — 프리팹 자체 크기를 바꾸면 내부 배치가 흐트러진다
                var cell = NewRect("Cell", grid);
                var ui   = Instantiate(cardPrefab, cell);

                if (pick == null)
                {
                    ui.Setup(card, usable: true);   // 보기 전용 — 클릭 구독이 없어 눌러도 아무 일 없다
                    // 호버하면 다음 강화 내용을 보여준다 (GDD 9장 — 덱 목록에서도 미리보기)
                    var captured = card;
                    TooltipTrigger.Attach(ui.gameObject, () => ("다음 강화", CardUpgrader.Preview(captured)));
                }
                else
                {
                    var captured = card;
                    bool can = pick.canPickCard == null || pick.canPickCard(card);
                    ui.Setup(card, usable: can);    // 고를 수 없으면 흐리게 (CardUI가 반투명 처리)
                    if (can) ui.OnClicked += _ => { var cb = pick?.onPickCard; Finish(); cb?.Invoke(captured); };
                    if (pick.cardTooltip != null)
                    {
                        var tip = pick.cardTooltip;
                        TooltipTrigger.Attach(ui.gameObject, () => (RarityTable.Colorize(captured.cardName, captured.rarity), tip(captured)));
                    }
                }

                var crt = ui.GetComponent<RectTransform>();
                crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot            = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = Vector2.zero;
                crt.localRotation    = Quaternion.identity;
                crt.localScale       = Vector3.one * CellScale;
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.verticalNormalizedPosition = 1f;   // 맨 위부터
    }

    // 동료 묶음 — 카드 프리팹이 아니라 이름·HP 타일로 그린다
    private void AddCompanionSection(IReadOnlyList<CompanionEntity> companions)
    {
        var header = NewRect("Header", content);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
        AddText(header.gameObject, 16, new Color(0.75f, 0.72f, 0.62f), TextAlignmentOptions.MidlineLeft)
            .text = $"동료 ({companions.Count}명)";

        if (companions.Count == 0)
        {
            var empty = NewRect("Empty", content);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            AddText(empty.gameObject, 16, new Color(0.6f, 0.58f, 0.52f), TextAlignmentOptions.MidlineLeft).text = "동료가 없다";
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            return;
        }

        var grid = NewRect("CompanionGrid", content);
        var g = grid.gameObject.AddComponent<GridLayoutGroup>();
        g.cellSize       = new Vector2(200f, 72f);
        g.spacing        = new Vector2(14f, 14f);
        g.constraint     = GridLayoutGroup.Constraint.Flexible;
        g.childAlignment = TextAnchor.UpperLeft;

        foreach (var c in companions)
        {
            if (c == null) continue;
            var captured = c;
            bool can = pick == null || pick.canPickCompanion == null || pick.canPickCompanion(c);

            var tile = NewRect("Companion", grid);
            var img  = tile.gameObject.AddComponent<Image>();
            img.color = can ? new Color(0.20f, 0.30f, 0.22f, 0.95f) : new Color(0.15f, 0.15f, 0.15f, 0.6f);

            var label = NewRect("Label", tile);
            label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(8f, 4f); label.offsetMax = new Vector2(-8f, -4f);
            AddText(label.gameObject, 16, can ? Color.white : new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center)
                .text = $"{c.EntityName}\nHP {c.Stats.CurrentHp}/{c.Stats.MaxHp}";

            if (pick != null && can)
            {
                var btn = tile.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => { var cb = pick?.onPickCompanion; Finish(); cb?.Invoke(captured); });
            }
            if (pick?.companionTooltip != null)
            {
                var tip = pick.companionTooltip;
                TooltipTrigger.Attach(tile.gameObject, () => (captured.EntityName, tip(captured)));
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    // ── 유틸 ────────────────────────────────────────────

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private TextMeshProUGUI AddText(GameObject go, int size, Color color, TextAlignmentOptions align)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize         = size;           // Neo둥근모는 16px 배수만
        tmp.fontStyle        = FontStyles.Normal;
        tmp.color            = color;
        tmp.alignment        = align;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget    = false;
        if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
        return tmp;
    }
}
