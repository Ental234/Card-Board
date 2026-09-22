using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 편성창의 칸 하나 — 진형 슬롯(1~4) 또는 대기열 항목
//
// 클릭과 드래그를 모두 받는다. 둘은 같은 판정을 공유하므로(FormationPanel.ApplyMove)
// 어느 쪽으로 조작하든 결과가 같다.
// Unity는 드래그가 성립하면 OnPointerClick을 부르지 않으므로 서로 간섭하지 않는다.
public class FormationSlotWidget : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // 1~4 = 진형 슬롯, GameManager.SlotReserve(0) = 대기열 항목
    public int Slot { get; private set; }

    // 대기열 항목일 때의 동료. 진형 슬롯이면 그 칸의 점유자(없으면 null)
    public CombatEntity Occupant { get; private set; }

    private FormationPanel owner;
    private GameObject     ghost;

    public void Bind(FormationPanel panel, int slot, CombatEntity occupant)
    {
        owner    = panel;
        Slot     = slot;
        Occupant = occupant;

        if (occupant != null)
            TooltipTrigger.Attach(gameObject, () => OccupantTooltip(Occupant));
    }

    private static (string, string) OccupantTooltip(CombatEntity unit)
    {
        if (unit == null) return ("", "");

        var body = new System.Text.StringBuilder();
        body.Append($"HP {unit.Stats.CurrentHp}/{unit.Stats.MaxHp}");

        if (unit is PlayerCharacter)
        {
            body.Append("\n플레이어 - 대기열로 내릴 수 없다.");
            return (unit.EntityName, body.ToString());
        }

        var data = (unit as CompanionEntity)?.Data;
        if (data != null)
        {
            if (data.isImmobile) body.Append("\n이동 불가 - 전투 중 자리를 옮길 수 없다.");
            if (!string.IsNullOrEmpty(data.description)) body.Append("\n\n").Append(data.description);
        }

        // 동료는 자동 행동한다 — 무엇을 하는지가 편성 판단의 전부다
        foreach (var p in unit.Patterns)
        {
            if (p == null) continue;
            body.Append($"\n\n[{p.patternName}]");
            if (!string.IsNullOrEmpty(p.description)) body.Append($" {p.description}");
        }

        return (unit.EntityName, body.ToString());
    }

    // 옮길 대상이 없는 칸은 드래그를 시작할 수 없다
    private bool CanDrag => Occupant != null && owner != null;

    // ── 클릭 ────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleClick(this);
    }

    // ── 드래그 ──────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!CanDrag) return;
        TooltipView.Hide(GetComponent<TooltipTrigger>());   // 끌고 다니는 동안 설명이 따라다니지 않게
        ghost = owner.CreateDragGhost(Occupant, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (ghost == null) return;
        ghost.transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 고스트를 먼저 치운다 — 아래에서 편성이 바뀌면 이 위젯 자체가 파괴된다
        if (ghost != null)
        {
            Destroy(ghost);
            ghost = null;
        }

        if (!CanDrag) return;
        owner.HandleDrop(this, eventData);
    }

    private void OnDisable()
    {
        // 드래그 도중 위젯이 사라지면 고스트가 화면에 남는다
        if (ghost == null) return;
        Destroy(ghost);
        ghost = null;
    }
}
