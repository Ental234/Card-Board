using System;
using System.Collections.Generic;
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

    // CombatPanel이 구독 → CombatManager.TryPlayCard() 호출
    public event Action<CardData, CombatEntity, CombatEntity> OnTargetSelected;

    private CardData     pendingCard;
    private CombatEntity pendingUser;
    private bool         isAoePending;

    private SlotSystem   slots;          // BindCombat에서 받아둔다
    private CombatEntity selected;       // 이동시킬 아군
    private readonly List<GameObject> moveArrows = new();

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

        // 선택한 유닛이 살아 있으면 새 위치에 화살표를 다시 그린다
        if (selected != null && selected.IsActive) ShowMoveArrows(selected);
        else                                       ClearSelection();
    }

    public void UnbindAll()
    {
        if (slots != null) slots.OnSlotsChanged -= Rebind;
        slots = null;

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
