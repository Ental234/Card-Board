using System;
using System.Collections.Generic;
using UnityEngine;

// 양 진영 4슬롯 배치 관리
// 슬롯 번호: 1(최전방) ~ 4(최후방) — 1-based
// CombatManager가 소유하며 카드 타겟팅·이동 검증에 사용
public class SlotSystem : MonoBehaviour
{
    private readonly CombatEntity[] playerSlots = new CombatEntity[4]; // index = slot-1
    private readonly CombatEntity[] enemySlots  = new CombatEntity[4];

    // ── 배치 & 제거 ─────────────────────────────────────

    public bool PlaceEntity(CombatEntity entity, bool isPlayerSide, int slot)
    {
        var slots = GetSlotsArray(isPlayerSide);
        int idx   = slot - 1;
        if (idx < 0 || idx >= 4) return false;
        if (slots[idx] != null)  return false;  // 이미 점유

        slots[idx] = entity;
        entity.SetSlot(slot);
        RaiseSlotsChanged();
        return true;
    }

    public void RemoveEntity(CombatEntity entity)
    {
        var slots = GetSlotsArray(entity.IsPlayerSide);
        int idx   = entity.CurrentSlot - 1;
        if (idx >= 0 && idx < 4 && slots[idx] == entity)
        {
            slots[idx] = null;
            RaiseSlotsChanged();
        }
    }

    // ── 이동 ────────────────────────────────────────────

    // 슬롯 구성이 바뀔 때마다 발생 — UI가 구독해 다시 바인딩한다
    public event Action OnSlotsChanged;

    // 미리보기 중에는 이벤트를 막는다. 안 그러면 UI가 가상 배치를 진짜인 줄 알고 다시 그린다.
    private int suppressDepth;

    private void RaiseSlotsChanged()
    {
        if (suppressDepth > 0) return;
        OnSlotsChanged?.Invoke();
    }

    // 실제로 옮기지 않고 "옮겼다면 어떻게 되는지"만 계산한다.
    //
    // 행동력을 쓰기 전에 결과를 보여주기 위한 것이다. 자리를 잠깐 바꿔 action을 돌리고
    // finally에서 반드시 원래대로 되돌린다 — 중간에 예외가 나도 배치가 틀어지면 안 된다.
    // 이동할 수 없는 방향이면 현재 상태 그대로 action만 돌린다.
    public void SimulateMove(CombatEntity entity, int direction, Action action)
    {
        if (action == null) return;

        if (entity == null || !CanMoveOrSwap(entity, direction, out var swapWith))
        {
            action();
            return;
        }

        var slots = GetSlotsArray(entity.IsPlayerSide);
        int from  = entity.CurrentSlot;
        int to    = from - direction;

        suppressDepth++;
        try
        {
            slots[from - 1] = swapWith;
            slots[to   - 1] = entity;
            entity.SetSlot(to);
            swapWith?.SetSlot(from);

            action();
        }
        finally
        {
            slots[from - 1] = entity;
            slots[to   - 1] = swapWith;
            entity.SetSlot(from);
            swapWith?.SetSlot(to);

            suppressDepth--;
        }
    }

    // direction: +1 = 전방(슬롯 감소), -1 = 후방(슬롯 증가)
    //
    // 이동 가능 여부를 미리 판정한다. 목적지에 같은 진영 유닛이 있으면
    // 막지 않고 자리를 바꾼다(swapWith에 상대를 돌려준다).
    public bool CanMoveOrSwap(CombatEntity entity, int direction, out CombatEntity swapWith)
    {
        swapWith = null;

        if (entity == null || !entity.CanMove) return false;    // 쓰러졌거나 고정된 유닛
        if (direction == 0) return false;

        int targetSlot = entity.CurrentSlot - direction;
        if (targetSlot < 1 || targetSlot > 4) return false;     // 보드 밖

        var occupant = GetSlotsArray(entity.IsPlayerSide)[targetSlot - 1];
        if (occupant == null)   return true;                    // 빈 칸 — 그냥 이동
        if (occupant == entity) return false;

        // 다른 진영과는 자리를 바꿀 수 없다 (지금 구조상 같은 배열이 아니라 발생하지 않지만 방어)
        if (occupant.IsPlayerSide != entity.IsPlayerSide) return false;

        // 고정된 유닛은 밀어낼 수 없다. 쓰러진 유닛은 치울 수 있다.
        if (!occupant.CanBeSwapped) return false;

        swapWith = occupant;
        return true;
    }

    // 빈 칸이면 이동, 아군이 있으면 자리 교체
    public bool MoveOrSwap(CombatEntity entity, int direction)
    {
        if (!CanMoveOrSwap(entity, direction, out var swapWith)) return false;

        var slots = GetSlotsArray(entity.IsPlayerSide);
        int from  = entity.CurrentSlot;
        int to    = from - direction;

        slots[from - 1] = swapWith;   // 교체 상대가 없으면 null이 들어가 빈 칸이 된다
        slots[to   - 1] = entity;

        entity.SetSlot(to);
        swapWith?.SetSlot(from);

        RaiseSlotsChanged();
        return true;
    }

    // ── 조회 ────────────────────────────────────────────

    public CombatEntity GetEntityAt(bool isPlayerSide, int slot)
    {
        var slots = GetSlotsArray(isPlayerSide);
        int idx   = slot - 1;
        return (idx >= 0 && idx < 4) ? slots[idx] : null;
    }

    // 진영 전체 활성 엔티티
    public List<CombatEntity> GetAllActive(bool isPlayerSide)
    {
        var result = new List<CombatEntity>();
        foreach (var e in GetSlotsArray(isPlayerSide))
            if (e != null && e.IsActive) result.Add(e);
        return result;
    }

    // SlotMask에 해당하는 슬롯의 활성 엔티티 목록
    public List<CombatEntity> GetEntitiesInSlots(bool isPlayerSide, SlotMask mask)
    {
        var slots  = GetSlotsArray(isPlayerSide);
        var result = new List<CombatEntity>();
        for (int i = 0; i < 4; i++)
        {
            SlotMask slotFlag = (SlotMask)(1 << i);
            if ((mask & slotFlag) != 0 && slots[i] != null && slots[i].IsActive)
                result.Add(slots[i]);
        }
        return result;
    }

    // 도발 중인 엔티티 (없으면 null)
    public CombatEntity GetTauntTarget(bool isPlayerSide)
    {
        foreach (var e in GetSlotsArray(isPlayerSide))
            if (e != null && e.IsActive && e.HasTaunt) return e;
        return null;
    }

    // ── 카드 유효 타겟 계산 ─────────────────────────────

    // user가 card를 쓸 때의 유효 타겟 목록 반환
    // AoE: 대상 슬롯 전체 / 단일 타겟: 도발 우선
    public List<CombatEntity> GetValidTargets(CombatEntity user, CardData card)
    {
        if (!CanUseFromSlot(user, card)) return new List<CombatEntity>();

        bool targetPlayerSide = card.targetAlly ? user.IsPlayerSide : !user.IsPlayerSide;
        var  candidates       = GetEntitiesInSlots(targetPlayerSide, card.targetSlots);

        if (!card.isAoe && !card.targetAlly)
        {
            // 단일 공격 → 도발 체크
            var taunt = GetTauntTarget(targetPlayerSide);
            if (taunt != null && candidates.Contains(taunt))
                return new List<CombatEntity> { taunt };
        }

        return candidates;
    }

    // 사용자의 현재 슬롯이 card.useableSlots에 포함되는지 확인
    public bool CanUseFromSlot(CombatEntity user, CardData card)
    {
        if (card.useableSlots == SlotMask.None) return true;  // 슬롯 제한 없음
        SlotMask userFlag = (SlotMask)(1 << (user.CurrentSlot - 1));
        return (card.useableSlots & userFlag) != 0;
    }

    // ── 행동 패턴 타겟 계산 (동료·적 공용) ─────────────
    //
    // 카드의 GetValidTargets와 대칭 구조다. 도발 규칙도 동일하게 맞춘다.

    // SlotMask에 해당하는 엔티티 — 쓰러진 유닛도 포함 (소생 패턴용)
    public List<CombatEntity> GetEntitiesInSlotsRaw(bool isPlayerSide, SlotMask mask)
    {
        var slots  = GetSlotsArray(isPlayerSide);
        var result = new List<CombatEntity>();
        for (int i = 0; i < 4; i++)
        {
            SlotMask slotFlag = (SlotMask)(1 << i);
            if ((mask & slotFlag) != 0 && slots[i] != null)
                result.Add(slots[i]);
        }
        return result;
    }

    // 두 유닛 사이의 거리
    //
    // 슬롯 번호가 곧 중앙선으로부터의 칸 수다. 마주보는 최전방끼리가 1(인접).
    //
    //   [플레이어]  4   3   2   1  │  1   2   3   4  [적]
    //                             중앙선
    //
    // 다른 진영: 슬롯 번호의 합 - 1 / 같은 진영: 슬롯 번호의 차
    public int GetDistance(CombatEntity a, CombatEntity b)
    {
        if (a == null || b == null) return int.MaxValue;

        return a.IsPlayerSide == b.IsPlayerSide
             ? Mathf.Abs(a.CurrentSlot - b.CurrentSlot)
             : a.CurrentSlot + b.CurrentSlot - 1;
    }

    // 시전자의 현재 슬롯이 pattern.casterSlots에 포함되는지
    // 주의: None은 "제한 없음"이다 (CanUseFromSlot과 같은 규칙)
    public bool CanCastFromSlot(CombatEntity user, ActionPatternData pattern)
    {
        if (user == null || pattern == null)          return false;
        if (pattern.casterSlots == SlotMask.None)     return true;
        if (user.CurrentSlot < 1 || user.CurrentSlot > 4) return false;

        SlotMask userFlag = (SlotMask)(1 << (user.CurrentSlot - 1));
        return (pattern.casterSlots & userFlag) != 0;
    }

    // 패턴이 실제로 타격할 대상 목록. 비어 있으면 불발이다.
    //
    // ★ 이 함수는 반드시 "실행하는 순간"에 호출해야 한다.
    //   적 인텐트는 패턴만 미리 고정하고 타겟은 여기서 다시 계산한다 —
    //   그래야 플레이어가 슬롯을 옮겨 공격을 피할 수 있다.
    public List<CombatEntity> GetPatternTargets(CombatEntity user, ActionPatternData pattern)
    {
        var result = new List<CombatEntity>();

        if (user == null || pattern == null)  return result;
        if (!CanCastFromSlot(user, pattern))  return result;

        // 자기 대상 — 진영·슬롯 규칙을 전부 건너뛴다
        if (pattern.targetMode == TargetMode.Self)
        {
            result.Add(user);
            return result;
        }

        bool targetPlayerSide = pattern.targetAlly ? user.IsPlayerSide : !user.IsPlayerSide;

        var candidates = pattern.includeKnockedOut
                       ? GetEntitiesInSlotsRaw(targetPlayerSide, pattern.targetSlots)
                       : GetEntitiesInSlots(targetPlayerSide, pattern.targetSlots);

        if (candidates.Count == 0) return result;  // 노린 슬롯이 전부 비었다 — 불발

        // 도발은 단일 공격에만 적용하고, 도발 대상이 후보에 있을 때만 좁힌다.
        // 후보에 없으면 무시 — 덕분에 targetSlots = Back 후열 저격이 도발을 자연히 뚫는다.
        if (pattern.respectTaunt && !pattern.isAoe && !pattern.targetAlly)
        {
            var taunt = GetTauntTarget(targetPlayerSide);
            if (taunt != null && candidates.Contains(taunt))
            {
                result.Add(taunt);
                return result;
            }
        }

        if (pattern.isAoe)
        {
            result.AddRange(candidates);
            return result;
        }

        SortByTargetMode(candidates, user, pattern.targetMode);

        int take = Mathf.Clamp(pattern.maxTargets, 1, candidates.Count);
        for (int i = 0; i < take; i++)
            result.Add(candidates[i]);

        return result;
    }

    // 우선순위가 높은 대상이 앞에 오도록 정렬한다.
    // 동률은 전부 전방 우선(슬롯 번호가 작은 쪽)으로 갈라 결과를 예측 가능하게 만든다.
    private void SortByTargetMode(List<CombatEntity> list, CombatEntity user, TargetMode mode)
    {
        switch (mode)
        {
            case TargetMode.Slots:
                list.Sort((a, b) => a.CurrentSlot.CompareTo(b.CurrentSlot));
                break;

            case TargetMode.Nearest:
                list.Sort((a, b) =>
                {
                    int c = GetDistance(user, a).CompareTo(GetDistance(user, b));
                    return c != 0 ? c : a.CurrentSlot.CompareTo(b.CurrentSlot);
                });
                break;

            case TargetMode.Farthest:
                list.Sort((a, b) =>
                {
                    int c = GetDistance(user, b).CompareTo(GetDistance(user, a));
                    return c != 0 ? c : a.CurrentSlot.CompareTo(b.CurrentSlot);
                });
                break;

            case TargetMode.LowestHp:
                list.Sort((a, b) =>
                {
                    int c = a.Stats.CurrentHp.CompareTo(b.Stats.CurrentHp);
                    return c != 0 ? c : a.CurrentSlot.CompareTo(b.CurrentSlot);
                });
                break;

            case TargetMode.Random:
                // Fisher-Yates. i가 1 이상이라 Random.Range(0, i + 1)의 min < max가 보장된다
                // (min >= max면 min을 그대로 반환해 셔플이 깨진다)
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
                break;
        }
    }

    // ── 리셋 ────────────────────────────────────────────

    public void ClearAll()
    {
        Array.Clear(playerSlots, 0, 4);
        Array.Clear(enemySlots,  0, 4);
    }

    // ── 내부 유틸 ────────────────────────────────────────

    private CombatEntity[] GetSlotsArray(bool isPlayerSide)
        => isPlayerSide ? playerSlots : enemySlots;
}
