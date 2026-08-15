using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 전투 슬롯 한 칸 — CombatSlotGridUI가 8개 보유
// 유닛 바인딩 시 CombatStats 이벤트를 구독해 실시간 갱신
public class SlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image           portraitImage;
    [SerializeField] private Slider          hpSlider;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI shieldText;
    [SerializeField] private GameObject      emptyRoot;      // 슬롯 비었을 때 표시
    [SerializeField] private GameObject      occupiedRoot;   // 유닛 있을 때 표시
    [SerializeField] private Image           highlightBorder; // 타겟 가능 시 하이라이트
    [SerializeField] private Image           knockedOutOverlay; // 쓰러짐(HP 0) 시 어둡게
    [SerializeField] private Image           padImage;        // 바닥 패드 (진영별로 교체)

    public event Action<SlotUI> OnClicked;

    public CombatEntity Occupant     { get; private set; }
    public bool         IsTargetable { get; private set; }

    // 런타임에 만드는 라벨(의도 배지·위협 수치)이 같은 폰트를 쓰도록 넘겨준다.
    // Neo둥근모를 유지해야 화면 안에서 글자가 따로 놀지 않는다.
    public TMP_FontAsset LabelFont => hpText != null ? hpText.font : null;

    // ── 바닥 패드 ───────────────────────────────────────

    // 프리팹 오버라이드는 되돌아가기 쉬우므로 진영별 패드는 런타임에 지정한다.
    // CombatSlotGridUI가 자기 배열 위치에 맞는 스프라이트를 넘긴다.
    public void SetPadSprite(Sprite sprite)
    {
        if (padImage == null || sprite == null) return;
        padImage.sprite = sprite;
    }

    // ── 바인딩 ──────────────────────────────────────────

    public void Bind(CombatEntity entity)
    {
        Occupant = entity;

        bool hasUnit = entity != null;
        emptyRoot?   .SetActive(!hasUnit);
        occupiedRoot?.SetActive(hasUnit);
        SetHighlight(false);

        // 쓰러짐 표시는 슬롯이 아니라 유닛의 상태다.
        // 자리 교체로 다른 유닛이 들어오면 반드시 다시 판정해야 한다.
        SetKnockedOut(entity != null && !entity.Stats.IsAlive);

        if (entity == null) return;

        if (portraitImage != null && entity.Portrait != null)
            portraitImage.sprite = entity.Portrait;

        RefreshHp(entity.Stats.CurrentHp, entity.Stats.MaxHp);
        RefreshShield(entity.Stats.Shield);

        entity.Stats.OnHpChanged     += RefreshHp;
        entity.Stats.OnShieldChanged += RefreshShield;
        entity.Stats.OnKnockedOut    += HandleKnockedOut;
    }

    public void Unbind()
    {
        if (Occupant != null)
        {
            Occupant.Stats.OnHpChanged     -= RefreshHp;
            Occupant.Stats.OnShieldChanged -= RefreshShield;
            Occupant.Stats.OnKnockedOut    -= HandleKnockedOut;
        }
        Occupant = null;
        emptyRoot?.SetActive(true);
        occupiedRoot?.SetActive(false);
        SetHighlight(false);
        SetKnockedOut(false);
    }

    // ── 갱신 ────────────────────────────────────────────

    private void RefreshHp(int current, int max)
    {
        if (hpSlider != null) hpSlider.value = max > 0 ? (float)current / max : 0f;
        if (hpText   != null) hpText.text    = $"{current}/{max}";

        // 쓰러짐 표시를 HP에서 파생시킨다 — 1HP 복귀 시 자동으로 풀린다
        SetKnockedOut(current <= 0);
    }

    private void RefreshShield(int shield)
    {
        if (shieldText == null) return;
        shieldText.gameObject.SetActive(shield > 0);
        shieldText.text = shield > 0 ? shield.ToString() : "";
    }

    private void HandleKnockedOut() => SetKnockedOut(true);

    private void SetKnockedOut(bool on)
    {
        if (knockedOutOverlay != null) knockedOutOverlay.gameObject.SetActive(on);
    }

    // ── 타겟 하이라이트 ──────────────────────────────────

    public void SetTargetable(bool targetable)
    {
        IsTargetable = targetable;
        SetHighlight(targetable);
    }

    private void SetHighlight(bool on)
    {
        if (highlightBorder != null) highlightBorder.gameObject.SetActive(on);
    }

    // 타겟팅 중이 아닐 때도 알린다 — 포지션 이동을 위한 아군 선택에 쓰인다.
    // 클릭의 의미(타겟 지정 / 유닛 선택)는 CombatSlotGridUI가 판단한다.
    public void OnPointerClick(PointerEventData _)
    {
        OnClicked?.Invoke(this);
    }
}
