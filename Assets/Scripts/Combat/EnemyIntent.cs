// 적이 "다음 턴에 무엇을 할지" 미리 정해 둔 예고
// MonoBehaviour 아님 — CombatEntity가 소유하는 순수 C# 클래스 (CombatStats와 같은 방식)
//
// ★ 패턴만 고정하고 타겟은 실행 시점에 다시 계산한다.
//   플레이어가 인텐트를 보고 슬롯을 옮겨 공격을 피할 수 있어야 하기 때문이다.
//   아래 preview* 값은 결정 시점의 스냅샷일 뿐 UI 표시 전용이며,
//   실행 로직이 이걸 읽는 순간 "이동으로 회피"가 깨진다 — 절대 참조하지 말 것.
public class EnemyIntent
{
    public ActionPatternData pattern;

    // ── 아래는 전부 UI 표시용 스냅샷 ──
    public SlotMask previewSlots;  // 결정 시점에 노렸던 슬롯
    public int      previewValue;  // 예상 피해량(또는 회복량)
    public bool     isAttack;      // 아이콘 선택용
}
