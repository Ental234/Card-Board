using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 슬롯 8칸 배치 + 타겟 선택 + 포지션 직접 이동
//
// 클릭의 의미는 상황에 따라 갈린다.
//   · 카드 타겟팅 중 → 타겟 지정
//   · 그 외          → 아군 유닛 선택 (좌우 화살표로 이동)
public class CombatSlotGridUI : MonoBehaviour
{
    [Header("플레이어 진영 (index 0 = 슬롯 1 최전방)")]
    [SerializeField] private SlotUI[] playerSlots = new SlotUI[4];

    [Header("적 진영 (index 0 = 슬롯 1 최전방)")]
    [SerializeField] private SlotUI[] enemySlots = new SlotUI[4];

    [Header("진영별 바닥 패드")]
    [SerializeField] private Sprite playerPadSprite;
    [SerializeField] private Sprite enemyPadSprite;

    [Header("이동 화살표")]
    [SerializeField] private CombatManager combatManager;
    [SerializeField] private Color arrowColor     = new(1f, 0.85f, 0.35f);
    [SerializeField] private float arrowOffsetX   = 78f;   // 슬롯 중심에서 좌우로 띄우는 거리
    [SerializeField] private float arrowLength    = 44f;
    [SerializeField] private float arrowThickness = 12f;
    [SerializeField] private float arrowHeadSize  = 20f;

    [Header("적 의도 표시")]
    // 아트가 나오기 전까지는 색 도형 + 숫자로 돌린다.
    // 나중에 IntentIcon 스프라이트만 끼우면 색은 그대로 배경으로 쓸 수 있다.
    [SerializeField] private float   intentOffsetY = 96f;   // 슬롯 중심에서 위로
    [SerializeField] private Vector2 intentSize    = new(56f, 32f);
    [SerializeField] private Vector2 threatSize    = new(48f, 32f);
    [SerializeField] private int     labelFontSize = 16;    // Neo둥근모는 16px 배수만 쓴다

    // CombatPanel이 구독 → CombatManager.TryPlayCard() 호출
    public event Action<CardData, CombatEntity, CombatEntity> OnTargetSelected;

    private CardData     pendingCard;
    private CombatEntity pendingUser;
    private bool         isAoePending;

    private SlotSystem   slots;          // BindCombat에서 받아둔다
    private CombatEntity selected;       // 이동시킬 아군
    private readonly List<GameObject> moveArrows   = new();
    private readonly List<GameObject> intentLabels = new();

    private void Awake()
    {
        foreach (var s in playerSlots)
        {
            s.OnClicked += HandleSlotClicked;
            s.SetPadSprite(playerPadSprite);
        }
        foreach (var s in enemySlots)
        {
            s.OnClicked += HandleSlotClicked;
            s.SetPadSprite(enemyPadSprite);
        }
    }

    // ── 전투 시작 시 바인딩 ──────────────────────────────

    public void BindCombat(SlotSystem slotSystem)
    {
        if (slots != null) slots.OnSlotsChanged -= Rebind;
        slots = slotSystem;
        slots.OnSlotsChanged += Rebind;   // 카드 효과로 자리가 바뀌어도 따라간다

        if (combatManager != null)
        {
            combatManager.OnIntentsRefreshed -= RedrawIntents;
            combatManager.OnIntentsRefreshed += RedrawIntents;
        }

        Rebind();
    }

    private void Rebind()
    {
        if (slots == null) return;

        for (int i = 0; i < 4; i++)
        {
            playerSlots[i].Bind(slots.GetEntityAt(isPlayerSide: true,  slot: i + 1));
            enemySlots[i] .Bind(slots.GetEntityAt(isPlayerSide: false, slot: i + 1));
        }

        RedrawIntents();

        // 선택한 유닛이 살아 있으면 새 위치에 화살표를 다시 그린다
        if (selected != null && selected.IsActive) ShowMoveArrows(selected);
        else                                       ClearSelection();
    }

    public void UnbindAll()
    {
        if (slots != null) slots.OnSlotsChanged -= Rebind;
        slots = null;

        if (combatManager != null) combatManager.OnIntentsRefreshed -= RedrawIntents;

        ClearIntentLabels();
        ClearSelection();
        foreach (var s in playerSlots) s.Unbind();
        foreach (var s in enemySlots)  s.Unbind();
    }

    // ── 타겟 선택 흐름 ───────────────────────────────────

    // 카드 클릭 시 CombatPanel이 호출
    public void BeginTargeting(CardData card, CombatEntity user, SlotSystem slotSystem)
    {
        CancelTargeting();

        // 자기 대상 카드는 고를 게 없으므로 즉시 발동한다.
        if (card.targetSelf)
        {
            if (slotSystem.CanUseFromSlot(user, card))
                OnTargetSelected?.Invoke(card, user, user);
            return;
        }

        pendingCard  = card;
        pendingUser  = user;
        isAoePending = card.isAoe;

        var validTargets = slotSystem.GetValidTargets(user, card);
        HighlightSlots(validTargets);

        // AoE는 타겟 선택 없이 즉시 발동 — 유효 타겟 그대로 이벤트 발생
        if (isAoePending)
        {
            foreach (var target in validTargets)
                OnTargetSelected?.Invoke(card, user, target);
            CancelTargeting();
        }
    }

    public void CancelTargeting()
    {
        pendingCard = null;
        pendingUser = null;
        ClearHighlights();
    }

    // ── 슬롯 클릭 ───────────────────────────────────────

    private void HandleSlotClicked(SlotUI slotUI)
    {
        // 카드 타겟팅 중이면 타겟 지정이 우선
        if (pendingCard != null)
        {
            // 하이라이트된 슬롯(= 유효 타겟)만 받는다.
            // 이 검사가 없으면 '타격'으로 아군을, '도발'로 적을 찍을 수 있다.
            if (!slotUI.IsTargetable || slotUI.Occupant == null) return;

            OnTargetSelected?.Invoke(pendingCard, pendingUser, slotUI.Occupant);
            CancelTargeting();
            return;
        }

        // 그 외에는 아군 선택 → 이동 화살표
        var occupant = slotUI.Occupant;
        if (occupant == null || !occupant.IsPlayerSide) { ClearSelection(); return; }
        if (occupant == selected)                        { ClearSelection(); return; }  // 다시 누르면 해제

        selected = occupant;
        ShowMoveArrows(occupant);
    }

    // ── 포지션 이동 화살표 ───────────────────────────────

    private void ShowMoveArrows(CombatEntity entity)
    {
        ClearArrows();
        if (combatManager == null) return;

        var slotUI = FindSlotUI(entity);
        if (slotUI == null) return;

        Vector2 center = slotUI.GetComponent<RectTransform>().anchoredPosition;

        // 화면상 오른쪽 = 전방(슬롯 번호 감소), 왼쪽 = 후방(슬롯 번호 증가)
        TryBuildArrow(entity, center, +1, +arrowOffsetX,   0f);
        TryBuildArrow(entity, center, -1, -arrowOffsetX, 180f);
    }

    private void TryBuildArrow(CombatEntity entity, Vector2 center, int direction,
                               float offsetX, float rotation)
    {
        // 갈 수 없는 방향이면 아예 안 띄운다 (이동 불가 판정)
        if (!combatManager.CanMoveFriendly(entity, direction)) return;

        var go = new GameObject($"MoveArrow_{direction}", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = center + new Vector2(offsetX, 0f);
        rt.sizeDelta        = new Vector2(arrowLength, arrowThickness);
        rt.localRotation    = Quaternion.Euler(0, 0, rotation);

        var img = go.AddComponent<Image>();
        img.color = arrowColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Move(entity, direction));

        // 화살촉 — 정사각형을 45도 돌려 끝에 붙인다
        var head = new GameObject("Head", typeof(RectTransform));
        head.transform.SetParent(go.transform, false);

        var hrt = head.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0.5f);
        hrt.pivot            = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta        = new Vector2(arrowHeadSize, arrowHeadSize);
        hrt.localRotation    = Quaternion.Euler(0, 0, 45f);

        var himg = head.AddComponent<Image>();
        himg.color         = arrowColor;
        himg.raycastTarget = false;

        moveArrows.Add(go);
    }

    private void Move(CombatEntity entity, int direction)
    {
        // 성공하면 SlotSystem.OnSlotsChanged → Rebind가 화살표를 다시 그린다
        if (!combatManager.TryMoveFriendly(entity, direction))
            ShowMoveArrows(entity);   // 실패 시(행동력 부족 등) 상태만 갱신
    }

    private SlotUI FindSlotUI(CombatEntity entity)
    {
        foreach (var s in playerSlots) if (s.Occupant == entity) return s;
        foreach (var s in enemySlots)  if (s.Occupant == entity) return s;
        return null;
    }

    private void ClearSelection()
    {
        selected = null;
        ClearArrows();
    }

    private void ClearArrows()
    {
        foreach (var go in moveArrows)
            if (go != null) Destroy(go);
        moveArrows.Clear();
    }

    // ── 적 의도 표시 ─────────────────────────────────────
    //
    // 두 가지를 그린다.
    //   적 슬롯   — 무엇을 할 것인가 (종류 색 + 수치)
    //   플레이어 슬롯 — 그래서 이 칸이 얼마나 맞는가 (합계 피해)
    //
    // 두 번째가 이 게임의 핵심이다. 적이 여럿이면 한 칸에 여러 공격이 겹치므로
    // CombatManager가 지금 배치 기준으로 합산해준다.

    private void RedrawIntents()
    {
        ClearIntentLabels();
        if (slots == null || combatManager == null) return;

        for (int i = 0; i < 4; i++)
        {
            // 적 배지
            var enemy = enemySlots[i].Occupant;
            var intent = enemy?.CurrentIntent;
            if (intent != null && enemy.IsActive)
            {
                string text = intent.previewValue > 0 ? intent.previewValue.ToString() : IntentLabel(intent.kind);
                BuildLabel(enemySlots[i], intentSize, IntentColor(intent.kind), Color.white, text);
            }

            // 플레이어 위협 수치.
            // 적 공격 배지와 같은 붉은색을 쓰면 어느 쪽 정보인지 헷갈리므로
            // 더 어둡게 깔고 글자를 노랗게 빼 "내가 맞을 양"임을 구분한다.
            int incoming = combatManager.GetIncomingDamage(i + 1);
            if (incoming > 0)
                BuildLabel(playerSlots[i], threatSize,
                           new Color(0.16f, 0.04f, 0.06f, 0.95f),
                           new Color(1f, 0.72f, 0.30f), incoming.ToString());
        }
    }

    private void BuildLabel(SlotUI slotUI, Vector2 size, Color background, Color textColor, string text)
    {
        Vector2 center = slotUI.GetComponent<RectTransform>().anchoredPosition;

        var go = new GameObject("IntentLabel", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center + new Vector2(0f, intentOffsetY);
        rt.sizeDelta        = size;

        var img = go.AddComponent<Image>();
        img.color         = background;
        img.raycastTarget = false;   // 슬롯 클릭을 가로채면 안 된다

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);

        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        var font  = slotUI.LabelFont;
        if (font != null) label.font = font;

        label.text                 = text;
        label.color                = textColor;
        label.fontSize             = labelFontSize;   // 비트맵 폰트라 배수를 지켜야 한다
        label.alignment            = TextAlignmentOptions.Center;
        label.enableAutoSizing     = false;
        label.raycastTarget        = false;
        label.fontStyle            = FontStyles.Normal;  // Neo둥근모는 볼드·이탤릭 금지

        intentLabels.Add(go);
    }

    private void ClearIntentLabels()
    {
        foreach (var go in intentLabels)
            if (go != null) Destroy(go);
        intentLabels.Clear();
    }

    // 수치가 없는 의도(도발·디버프 등)에 쓰는 짧은 한글 라벨.
    // Neo둥근모에 ※★→ 같은 기호가 없어 기호로 때울 수 없다.
    private static string IntentLabel(IntentKind kind)
    {
        switch (kind)
        {
            case IntentKind.Debuff:       return "저주";
            case IntentKind.Buff:         return "강화";
            case IntentKind.Heal:         return "회복";
            case IntentKind.Taunt:        return "도발";
            case IntentKind.SelfDestruct: return "자폭";
            default:                      return "행동";
        }
    }

    private static Color IntentColor(IntentKind kind)
    {
        switch (kind)
        {
            case IntentKind.Attack:       return new Color(0.72f, 0.13f, 0.13f, 0.92f);  // 붉은색
            case IntentKind.AttackAoe:    return new Color(0.86f, 0.35f, 0.10f, 0.92f);  // 주황
            case IntentKind.Debuff:       return new Color(0.44f, 0.20f, 0.60f, 0.92f);  // 보라
            case IntentKind.Buff:         return new Color(0.20f, 0.42f, 0.62f, 0.92f);  // 파랑
            case IntentKind.Heal:         return new Color(0.20f, 0.55f, 0.30f, 0.92f);  // 초록
            case IntentKind.Taunt:        return new Color(0.60f, 0.50f, 0.15f, 0.92f);  // 황토
            case IntentKind.SelfDestruct: return new Color(0.90f, 0.75f, 0.10f, 0.95f);  // 노랑 — 가장 눈에 띄게
            default:                      return new Color(0.35f, 0.35f, 0.35f, 0.92f);
        }
    }

    // ── 하이라이트 ───────────────────────────────────────

    private void HighlightSlots(List<CombatEntity> targets)
    {
        foreach (var s in playerSlots) s.SetTargetable(targets.Contains(s.Occupant));
        foreach (var s in enemySlots)  s.SetTargetable(targets.Contains(s.Occupant));
    }

    private void ClearHighlights()
    {
        foreach (var s in playerSlots) s.SetTargetable(false);
        foreach (var s in enemySlots)  s.SetTargetable(false);
    }
}
