using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 페이즈의 보스 정보 패널 (GDD 14장)
//
// **맵 위의 보스 아이콘에 호버하면 뜬다.** 보드 화면에 텍스트가 늘 떠 있으면 지저분하다 (실플레이 피드백).
// 패널 안의 하수인·렐릭 칩에도 툴팁이 있으므로, 아이콘에서 패널로 마우스를 옮기는 동안은 닫지 않는다.
//
// 보여주는 것: 이름 · 누적 HP · 공격력 · 주사위 · 속박 · 하수인 · 렐릭 · 최근 행동
// 보여주지 않는 것:
//   · **이동 성향** — 보스마다 어떻게 움직이는지 알아가는 것 자체가 플레이 동기다
//   · **조우지점까지 최단 거리** — 맵의 말 높이가 곧 남은 칸 수라 한눈에 읽힌다.
//     정확한 값이 필요할 때를 위해 이 패널에 보스·나를 **한 줄로 나란히** 적는다
//     (내 말 쪽에 따로 UI를 달면 보드가 지저분해진다)
//
// 씬을 건드리지 않고 BoardMapView가 런타임에 만든다. 값이 흩어진 곳(보드·보스·렐릭·GameManager)에서 오므로
// 이벤트를 전부 구독하는 대신 짧은 주기로 요약값을 비교해 바뀌었을 때만 다시 그린다.
public class BossInfoPanelUI : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler,
                                              UnityEngine.EventSystems.IPointerExitHandler
{
    private const float Width         = 380f;
    private const float PollInterval  = 0.2f;
    private const float LogDuration   = 4f;
    private const float HideGrace     = 0.15f;   // 아이콘 → 패널로 옮겨가는 틈에 닫히지 않게

    private static readonly Color TitleColor = new(1f, 0.86f, 0.50f);
    private static readonly Color TextColor  = new(0.92f, 0.90f, 0.85f);
    private static readonly Color DimColor   = new(0.65f, 0.62f, 0.56f);

    private BoardPhaseManager board;
    private TMP_FontAsset     font;

    private TextMeshProUGUI nameText;
    private RectTransform   hpFill;
    private TextMeshProUGUI hpText;
    private TextMeshProUGUI statText;
    private TextMeshProUGUI minionHeader;
    private RectTransform   minionGrid;
    private TextMeshProUGUI relicHeader;
    private RectTransform   relicGrid;
    private TextMeshProUGUI logText;

    private string lastSignature;
    private float  nextPoll;
    private int    lastBossRoll;
    private int    revealedBossRoll;
    private string lastLog = "";
    private float  logUntil;

    // 호버 표시
    private CanvasGroup   group;
    private RectTransform anchorIcon;
    private bool          overIcon;
    private bool          overPanel;
    private float         hideAt = -1f;
    private bool          shown;

    // ── 생성 ────────────────────────────────────────────

    public static BossInfoPanelUI Create(RectTransform parent, BoardPhaseManager boardPhaseManager,
                                         TMP_FontAsset fontAsset, RectTransform bossIcon)
    {
        var go = new GameObject("BossInfoPanel", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var panel = go.AddComponent<BossInfoPanelUI>();
        panel.board = boardPhaseManager;
        panel.font  = fontAsset;
        panel.Build();
        panel.Subscribe();
        panel.BindIcon(bossIcon);
        panel.SetShown(false);
        return panel;
    }

    private void OnDestroy() => Unsubscribe();

    private void Subscribe()
    {
        if (board == null) return;
        board.OnBossDiceRolled   += HandleBossDiceRolled;
        board.OnBossDiceRevealed += HandleBossDiceRevealed;
        board.OnEncounterLog     += HandleLog;
        board.OnSectionChanged   += HandleSectionChanged;
    }

    private void Unsubscribe()
    {
        if (board == null) return;
        board.OnBossDiceRolled   -= HandleBossDiceRolled;
        board.OnBossDiceRevealed -= HandleBossDiceRevealed;
        board.OnEncounterLog     -= HandleLog;
        board.OnSectionChanged   -= HandleSectionChanged;
    }

    private void HandleBossDiceRolled(int v)   { lastBossRoll = v; revealedBossRoll = 0; ForceRefresh(); }
    private void HandleBossDiceRevealed(int v) { revealedBossRoll = v; ForceRefresh(); }
    private void HandleLog(string msg)         { lastLog = msg; logUntil = Time.time + LogDuration; ForceRefresh(); }

    // 새 스테이지(구획 0)면 이전 보스의 주사위 기록을 지운다
    private void HandleSectionChanged(int section)
    {
        if (section == 0) { lastBossRoll = 0; revealedBossRoll = 0; lastLog = ""; }
        ForceRefresh();
    }

    private void ForceRefresh() => lastSignature = null;

    private void Build()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.sizeDelta        = new Vector2(Width, 0f);

        // 호버 때만 보이게 — 비활성화하지 않고 투명하게 둔다 (Update가 계속 돌아야 닫힘 판정을 한다)
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;

        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.06f, 0.05f, 0.92f);

        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor    = new Color(0.55f, 0.20f, 0.18f, 0.9f);   // 보스 쪽이라 붉은 테
        outline.effectDistance = new Vector2(2f, -2f);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(14, 14, 12, 12);
        layout.spacing                = 6f;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;

        var fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        nameText     = AddText("Name", TitleColor);
        BuildHpBar();
        statText     = AddText("Stats", TextColor);
        minionHeader = AddText("MinionHeader", DimColor);
        minionGrid   = AddGrid("Minions");
        relicHeader  = AddText("RelicHeader", DimColor);
        relicGrid    = AddGrid("Relics");
        logText      = AddText("Log", new Color(1f, 0.70f, 0.45f));
    }

    private TextMeshProUGUI AddText(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize         = 16;                  // Neo둥근모는 16px 배수만
        tmp.fontStyle        = FontStyles.Normal;
        tmp.color            = color;
        tmp.alignment        = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget    = false;
        if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
        return tmp;
    }

    private void BuildHpBar()
    {
        var bar = new GameObject("HpBar", typeof(RectTransform));
        bar.transform.SetParent(transform, false);
        bar.AddComponent<LayoutElement>().preferredHeight = 24f;

        var back = bar.AddComponent<Image>();
        back.color = new Color(0.20f, 0.06f, 0.06f, 1f);
        back.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(bar.transform, false);
        hpFill = fill.GetComponent<RectTransform>();
        hpFill.anchorMin = Vector2.zero;
        hpFill.anchorMax = Vector2.one;
        hpFill.offsetMin = hpFill.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.75f, 0.16f, 0.14f, 1f);
        fillImg.raycastTarget = false;

        var label = new GameObject("Text", typeof(RectTransform));
        label.transform.SetParent(bar.transform, false);
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        hpText = label.AddComponent<TextMeshProUGUI>();
        hpText.fontSize      = 16;
        hpText.alignment     = TextAlignmentOptions.Center;
        hpText.color         = Color.white;
        hpText.raycastTarget = false;
        if (font != null) { hpText.font = font; hpText.fontSharedMaterial = font.material; }
    }

    private RectTransform AddGrid(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var grid = go.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(112f, 28f);
        grid.spacing         = new Vector2(6f, 6f);
        grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go.GetComponent<RectTransform>();
    }

    // ── 갱신 ────────────────────────────────────────────

    // ── 호버 ────────────────────────────────────────────

    private void BindIcon(RectTransform icon)
    {
        anchorIcon = icon;
        if (icon == null) return;

        // 아이콘이 호버를 받으려면 레이캐스트 대상이어야 한다
        var graphic = icon.GetComponent<Graphic>();
        if (graphic != null) graphic.raycastTarget = true;

        var relay = HoverRelay.Attach(icon.gameObject);
        relay.OnEnter += () => { overIcon = true;  Open(); };
        relay.OnExit  += () => { overIcon = false; ScheduleHide(); };
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) { overPanel = true; hideAt = -1f; }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _)  { overPanel = false; ScheduleHide(); }

    private void Open()
    {
        hideAt = -1f;
        ForceRefresh();
        nextPoll = 0f;
        Update();            // 내용을 먼저 채워야 높이가 정해져 위치를 잡을 수 있다
        PlaceNearIcon();
        SetShown(true);
    }

    private void ScheduleHide()
    {
        if (!overIcon && !overPanel) hideAt = Time.time + HideGrace;
    }

    private void SetShown(bool value)
    {
        shown = value;
        if (group == null) return;
        group.alpha          = value ? 1f : 0f;
        group.blocksRaycasts = value;   // 닫혀 있을 때 맵 클릭을 가로채지 않게
    }

    // 보스 아이콘 오른쪽에 붙인다. 화면 밖으로 나가면 왼쪽·위로 민다
    private void PlaceNearIcon()
    {
        if (anchorIcon == null) return;

        var rt     = GetComponent<RectTransform>();
        var parent = rt.parent as RectTransform;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        var canvas = GetComponentInParent<Canvas>().rootCanvas;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        var screen = RectTransformUtility.WorldToScreenPoint(cam, anchorIcon.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local);

        float w = rt.rect.width, h = rt.rect.height;
        var half = parent.rect.size * 0.5f;
        const float gap = 36f, pad = 8f;

        float x = local.x + gap;
        if (x + w > half.x - pad) x = local.x - gap - w;          // 오른쪽이 모자라면 왼쪽에
        float y = local.y + h * 0.5f;                               // 아이콘 높이 가운데
        y = Mathf.Clamp(y, -half.y + pad + h, half.y - pad);

        rt.anchoredPosition = new Vector2(x, y);
        rt.SetAsLastSibling();
    }

    private void Update()
    {
        if (shown && hideAt > 0f && Time.time >= hideAt && !overIcon && !overPanel)
        {
            hideAt = -1f;
            SetShown(false);
        }

        if (Time.time < nextPoll) return;
        nextPoll = Time.time + PollInterval;

        if (lastLog.Length > 0 && Time.time > logUntil) { lastLog = ""; ForceRefresh(); }

        string sig = Signature();
        if (sig == lastSignature) return;
        lastSignature = sig;
        Redraw();
    }

    private string Signature()
    {
        var gm   = GameManager.Instance;
        var boss = gm != null ? gm.CurrentBoss : null;
        if (boss == null || board == null || board.TotalNodes == 0) return "none";

        return $"{boss.GetInstanceID()}|{boss.Stats.CurrentHp}|{boss.Stats.MaxHp}|{boss.Stats.BaseAttack}|" +
               $"{board.BossStepsToEncounter}|{board.PlayerStepsToEncounter}|{board.BossAtEncounter}|{board.PlayerAtEncounter}|" +
               $"{lastBossRoll}|{revealedBossRoll}|{board.BossFreezeTurns}|{gm.BossMinions.Count}|" +
               $"{RelicManager.Instance?.BossRelics.Count ?? 0}|{lastLog}";
    }

    private void Redraw()
    {
        var gm   = GameManager.Instance;
        var boss = gm != null ? gm.CurrentBoss : null;
        if (boss == null || board == null || board.TotalNodes == 0)
        {
            SetShown(false);
            return;
        }

        nameText.text = boss.EntityName;

        // HP — 조우를 거듭하며 깎이는 누적 피해가 보여야 한다
        int hp = boss.Stats.CurrentHp, max = Mathf.Max(1, boss.Stats.MaxHp);
        hpFill.anchorMax = new Vector2(Mathf.Clamp01((float)hp / max), 1f);
        hpText.text      = $"HP {hp} / {max}";

        var sb = new StringBuilder();
        sb.Append($"공격력 {boss.Stats.BaseAttack}\n");

        string bossDist = board.BossAtEncounter   ? "도착 - 무장 중" : $"{board.BossStepsToEncounter}칸";
        string myDist   = board.PlayerAtEncounter ? "도착 - 대기 중" : $"{board.PlayerStepsToEncounter}칸";
        sb.Append($"조우지점까지  보스 {bossDist} / 나 {myDist}");

        if (revealedBossRoll > 0)  sb.Append($"\n다음 주사위 {revealedBossRoll} (예언)");
        else if (lastBossRoll > 0) sb.Append($"\n직전 주사위 {lastBossRoll}");

        if (board.BossFreezeTurns > 0) sb.Append($"\n속박 - {board.BossFreezeTurns}턴 이동 불가");
        statText.text = sb.ToString();

        RedrawMinions(gm.BossMinions);
        RedrawRelics();

        logText.text = lastLog.Length > 0 ? $"최근: {lastLog}" : "";
        logText.gameObject.SetActive(lastLog.Length > 0);
    }

    private void RedrawMinions(IReadOnlyList<MinionData> minions)
    {
        Clear(minionGrid);
        minionHeader.text = $"하수인 {minions.Count}/{BoardPhaseManager.MaxBossMinions}" + (minions.Count == 0 ? " - 없음" : "");

        foreach (var m in minions)
        {
            if (m == null) continue;
            var captured = m;
            var chip = AddChip(minionGrid, m.minionName, new Color(0.35f, 0.12f, 0.12f, 0.95f));
            TooltipTrigger.Attach(chip, () => (captured.minionName, MinionDescription(captured)));
        }
        minionGrid.gameObject.SetActive(minions.Count > 0);
    }

    private void RedrawRelics()
    {
        Clear(relicGrid);
        var relics = RelicManager.Instance != null ? RelicManager.Instance.BossRelics : null;
        int count  = relics?.Count ?? 0;
        relicHeader.text = $"렐릭 {count}" + (count == 0 ? " - 없음" : "");

        if (relics != null)
        {
            foreach (var r in relics)
            {
                if (r == null) continue;
                var chip = AddChip(relicGrid, r.relicName, new Color(0.40f, 0.33f, 0.14f, 0.95f));
                TooltipTrigger.Attach(chip, RarityTable.Colorize(r.relicName, r.rarity), r.description);
            }
        }
        relicGrid.gameObject.SetActive(count > 0);
    }

    private static string MinionDescription(MinionData m)
    {
        var sb = new StringBuilder();
        sb.Append($"HP {m.maxHp} / 공격력 {m.baseAttack}");
        if (m.startsWithTaunt) sb.Append("\n도발 - 단일 대상 공격을 끌어당긴다");
        if (m.reviveCount > 0) sb.Append($"\n부활 {m.reviveCount}회");
        if (m.isImmobile)      sb.Append("\n이동 불가");

        foreach (var p in m.patterns)
        {
            if (p == null) continue;
            sb.Append($"\n\n[{p.patternName}]");
            if (!string.IsNullOrEmpty(p.description)) sb.Append($" {p.description}");
        }
        return sb.ToString();
    }

    private GameObject AddChip(RectTransform grid, string label, Color color)
    {
        var go = new GameObject("Chip", typeof(RectTransform));
        go.transform.SetParent(grid, false);

        var img = go.AddComponent<Image>();
        img.color = color;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4f, 0f);
        trt.offsetMax = new Vector2(-4f, 0f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text             = label;
        tmp.fontSize         = 16;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.color            = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode     = TextOverflowModes.Ellipsis;
        tmp.raycastTarget    = false;
        if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
        return go;
    }

    private static void Clear(RectTransform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
            child.SetActive(false);   // Destroy는 프레임 끝 — 레이아웃에 한 프레임 겹치지 않게
            Destroy(child);
        }
    }

    // 새 보드를 그릴 때 — 내용만 갱신한다 (보이기는 호버가 정한다)
    public void RefreshNow()
    {
        ForceRefresh();
        nextPoll = 0f;
    }
}
