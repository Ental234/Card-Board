using System;
using System.Collections.Generic;
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

    [Header("상태이상 칩")]
    // 아트가 나오기 전까지 색 + 한글 라벨로 돌린다.
    // Neo둥근모에 ※★ 같은 기호가 없어 아이콘을 글자로 때울 수 없다.
    [SerializeField] private Vector2 statusChipSize = new(52f, 22f);
    [SerializeField] private float   statusChipGap  = 4f;
    [SerializeField] private float   statusRowOffsetY = -104f;  // 슬롯 중심에서 아래로
    [SerializeField] private int     statusFontSize = 16;       // 16px 배수만 쓴다

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
        entity.Stats.OnStatusChanged += HandleStatusChanged;

        RefreshStatuses();
    }

    public void Unbind()
    {
        if (Occupant != null)
        {
            Occupant.Stats.OnHpChanged     -= RefreshHp;
            Occupant.Stats.OnShieldChanged -= RefreshShield;
            Occupant.Stats.OnKnockedOut    -= HandleKnockedOut;
            Occupant.Stats.OnStatusChanged -= HandleStatusChanged;
        }
        Occupant = null;
        emptyRoot?.SetActive(true);
        occupiedRoot?.SetActive(false);
        SetHighlight(false);
        SetKnockedOut(false);
        ClearStatusChips();
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

    // ── 상태이상 표시 ────────────────────────────────────
    //
    // 칩 하나에 색 + 한글 라벨 + 숫자. 숫자의 뜻은 종류에 따라 다르다.
    //   출혈·독·화상 → 매 턴 들어올 피해(스택)
    //   기절·도발·약화·취약 → 남은 지속 턴
    // 플레이어가 궁금한 건 각각 "얼마나 아픈가"와 "언제 풀리나"라서 이렇게 나눴다.

    // 표시 순서를 고정한다 — 딕셔너리 순서에 맡기면 칩이 매 턴 자리를 바꿔 읽기 어렵다.
    private static readonly StatusEffect[] StatusOrder =
    {
        StatusEffect.Stun,
        StatusEffect.Taunt,
        StatusEffect.Vulnerable,
        StatusEffect.Weak,
        StatusEffect.Poison,
        StatusEffect.Bleed,
        StatusEffect.Burn,
    };

    private readonly List<GameObject> statusChips = new();

    private void HandleStatusChanged(StatusEffect _, int __) => RefreshStatuses();

    private void RefreshStatuses()
    {
        ClearStatusChips();
        if (Occupant == null) return;

        var active = new List<StatusEffect>();
        foreach (var status in StatusOrder)
            if (Occupant.Stats.HasStatus(status)) active.Add(status);

        if (active.Count == 0) return;

        float totalWidth = active.Count * statusChipSize.x + (active.Count - 1) * statusChipGap;
        float startX     = -totalWidth * 0.5f + statusChipSize.x * 0.5f;

        for (int i = 0; i < active.Count; i++)
        {
            float x = startX + i * (statusChipSize.x + statusChipGap);
            BuildStatusChip(active[i], new Vector2(x, statusRowOffsetY));
        }
    }

    private void BuildStatusChip(StatusEffect status, Vector2 offset)
    {
        var go = new GameObject("Status_" + status, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta        = statusChipSize;

        var img = go.AddComponent<Image>();
        img.color         = StatusColor(status);
        img.raycastTarget = false;   // 슬롯 클릭을 가로채면 안 된다

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);

        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        if (hpText != null) label.font = hpText.font;   // Neo둥근모 유지

        label.text             = StatusLabel(status) + " " + StatusValue(status);
        label.color            = Color.white;
        label.fontSize         = statusFontSize;
        label.alignment        = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.raycastTarget    = false;
        label.fontStyle        = FontStyles.Normal;   // 볼드·이탤릭 금지

        statusChips.Add(go);
    }

    private int StatusValue(StatusEffect status)
    {
        switch (status)
        {
            // 매 턴 들어올 피해가 곧 스택이다
            case StatusEffect.Bleed:
            case StatusEffect.Poison:
            case StatusEffect.Burn:
                return Occupant.Stats.GetStatusStacks(status);

            // 나머지는 언제 풀리는지가 중요하다
            default:
                return Occupant.Stats.GetStatusDuration(status);
        }
    }

    private void ClearStatusChips()
    {
        foreach (var go in statusChips)
        {
            if (go == null) continue;

            // Destroy는 프레임 끝에 처리되므로 먼저 꺼야 새 칩과 한 프레임 겹치지 않는다
            go.SetActive(false);
            Destroy(go);
        }
        statusChips.Clear();
    }

    private static string StatusLabel(StatusEffect status)
    {
        switch (status)
        {
            case StatusEffect.Bleed:      return "출혈";
            case StatusEffect.Poison:     return "독";
            case StatusEffect.Burn:       return "화상";
            case StatusEffect.Stun:       return "기절";
            case StatusEffect.Taunt:      return "도발";
            case StatusEffect.Weak:       return "약화";
            case StatusEffect.Vulnerable: return "취약";
            default:                      return "";
        }
    }

    private static Color StatusColor(StatusEffect status)
    {
        switch (status)
        {
            case StatusEffect.Bleed:      return new Color(0.65f, 0.10f, 0.16f, 0.95f);
            case StatusEffect.Poison:     return new Color(0.30f, 0.55f, 0.18f, 0.95f);
            case StatusEffect.Burn:       return new Color(0.85f, 0.40f, 0.10f, 0.95f);
            case StatusEffect.Stun:       return new Color(0.55f, 0.52f, 0.18f, 0.95f);
            case StatusEffect.Taunt:      return new Color(0.72f, 0.45f, 0.12f, 0.95f);
            case StatusEffect.Weak:       return new Color(0.32f, 0.38f, 0.52f, 0.95f);
            case StatusEffect.Vulnerable: return new Color(0.55f, 0.20f, 0.62f, 0.95f);
            default:                      return new Color(0.35f, 0.35f, 0.35f, 0.95f);
        }
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
