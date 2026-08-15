# 행동 패턴 시스템

동료·적·보스가 **같은 엔진**으로 싸운다. 유닛 종류별 코드가 따로 있지 않다.

---

## 1. 하드코딩인가, 데이터인가

**전부 데이터다.** 패턴은 `ActionPatternData` ScriptableObject 에셋이고, 보스 전용 코드는 없다.

### 재사용이 실제로 일어나는 증거

`Assets/Data/Patterns/Pattern_기본공격.asset` **하나**를 하수인 4종이 공유한다.

```
Minion_고블린 / Minion_돌골렘 / Minion_저주정령 / Minion_해골병사
```

복사본이 아니라 같은 에셋을 참조하므로, 이 패턴의 피해를 바꾸면 넷이 동시에 바뀐다.
반대로 `Pattern_마왕_흑염`을 하수인의 `patterns`에 끌어다 놓으면 그대로 작동한다 — 보스 전용 표식이 없다.

### 계층별 결합도

| 요소 | 위치 | 보스 전용인가 |
|---|---|---|
| `ActionPatternData` (SO 정의) | `Assets/Scripts/Combat/ActionPatternData.cs` | ✗ 공용 |
| `GetPatternTargets` (타겟 계산) | `SlotSystem.cs` | ✗ 공용 |
| `ApplyPatternEffects` (효과 실행) | `CombatManager.cs` | ✗ 공용 |
| `RunEntityPatterns` (동료·즉시 발동) | `CombatManager.cs` | ✗ 공용 |
| `DecideEnemyIntents` (적·예고 결정) | `CombatManager.cs` | ✗ 공용 |
| `MeetsCondition` (추가 조건) | `CombatManager.cs` | **△ 한 군데만** |

### 유일한 결합 지점

`CombatManager.MeetsCondition`:

```csharp
// 페이즈·패닉은 보스만 갖는 개념이다
if (pattern.minPhase > 0 || pattern.requirePanic)
{
    var bossEntity = entity as BossEntity;
    if (bossEntity == null) return false;
    ...
}
```

`CurrentPhase` / `IsPanicMode`가 `BossEntity`에만 있어 여기서 캐스팅한다. 결과:

- **`hpBelowRatio`는 누구에게나 작동한다** (`Stats`만 보므로). 장군의 처형 패턴이 그 예이고, 하수인에게 붙여도 된다.
- **`minPhase` / `requirePanic`은 보스에게만 작동한다.** 하수인에 붙이면 캐스팅이 실패해 영원히 발동하지 않는다 — 조용히 안 나올 뿐 에러는 아니다.

**일반화가 필요해지면**(예: 페이즈를 가진 정예 몬스터), `CurrentPhase`/`IsPanicMode`를 `CombatEntity`로 올리거나 작은 인터페이스로 빼면 이 캐스팅만 사라진다. 나머지 코드는 손댈 게 없다.

---

## 2. 패턴 한 장의 구조

"**언제**(timing) + **누구를**(targetMode) + **무엇을**(effects)"

### 발동

| 필드 | 뜻 |
|---|---|
| `timing` | `TurnStart`/`TurnEnd`(동료) · `OnPlayerAttack`/`OnPlayerDefend`/`OnPlayerDamaged`(반응) · `OwnTurn`(적) |
| `cooldown` | 발동 후 쉬는 턴 수 (0 = 매 턴) |
| `initialCooldown` | 전투 시작 후 첫 발동까지 대기 턴 수 |
| `maxUsesPerCombat` | 전투당 최대 횟수 (0 = 무제한) |
| `priority` | 적 인텐트 선택 우선순위 (높을수록 우선, 동률이면 무작위) |

### 추가 조건 (0 / false = 조건 없음)

| 필드 | 뜻 | 적용 대상 |
|---|---|---|
| `hpBelowRatio` | 시전자 HP가 이 비율 이하일 때만 | 전체 |
| `minPhase` | 보스 페이즈 N 이상 | **보스만** |
| `requirePanic` | 보스 패닉 모드 | **보스만** |
| `casterSlots` | 시전자가 이 슬롯에 있을 때만 (`None` = 제한 없음) | 전체 |

### 타겟

| 필드 | 뜻 |
|---|---|
| `targetMode` | `Slots`(고정) · `Nearest`/`Farthest`(거리) · `LowestHp` · `Self` · `Random` |
| `targetSlots` | 후보 슬롯 (`None` = 후보 0개 = 불발) |
| `targetAlly` | true면 아군 대상 |
| `isAoe` | true면 후보 전원 |
| `maxTargets` | 단일 대상일 때 최대 수 |
| `respectTaunt` | false면 도발 무시 (후열 저격) |
| `includeKnockedOut` | true면 쓰러진 유닛도 후보 (소생) |

거리 기준 모드도 `targetSlots`로 1차 필터를 건 뒤 고른다 → "적 후열 중 가장 가까운 놈" 같은 조합이 가능하다.

### 효과

`CardEffect[]` — **카드와 같은 타입을 그대로 재사용한다.**
`Damage` / `Heal` / `Shield` / `ApplyStatus` / `MovePosition` / `DrawCard` / `GainEnergy`

`selfDestructAfterUse` = 효과를 다 적용한 뒤 시전자가 스스로 쓰러진다 (자폭).

---

## 3. 반드시 알아야 할 함정

### 피해 수치는 가산이다

```
실제 피해 = CalculateAttack(value) = value + 시전자 BaseAttack   (약화면 ×0.75)
```

- `value = 0` → **공격력만큼**
- 광역기를 약하게 만들려면 **음수 value**를 쓴다 (마왕 흑염 `-8` → 16-8 = 8).
  `Max(0, …)`로 걸러져 음수 피해는 나오지 않는다.
- 인스펙터에서 읽기 나쁘다는 걸 알고 쓰는 것이다 — `PLAN.md` 검토 대기 참고.

### `MovePosition`의 value는 칸 수 델타다

한 칸씩만 움직이는 게 아니다. `MoveOrSwap`이 목적지 범위만 검사하므로 **여러 칸 점프가 된다.**
마왕의 강림이 슬롯4에서 `value 3`으로 슬롯1까지 한 번에 내려오는 게 이 원리다 (4 - 3 = 1).

그리고 움직이는 건 **타겟이 아니라 시전자**다.

### `None`의 의미가 필드마다 반대다

- `casterSlots = None` → **제한 없음**
- `targetSlots = None` → **후보 0개 = 불발**

그래서 `targetSlots` 기본값이 `All`이다. 새 에셋을 만들자마자 불발하는 사고를 막는다.

### SO에 런타임 상태를 넣지 말 것

쿨다운·잔여 횟수는 전부 `CombatEntity`가 들고 있다. SO에 두면 같은 에셋을 공유하는
하수인끼리 쿨다운이 엉키고, 에디터에서는 플레이 종료 후에도 값이 남는다.

### 열거형에 번호를 명시할 것

Unity는 열거형을 정수로 직렬화한다. `TriggerTiming`/`TargetMode` 중간에 값을 끼워 넣으면
**이미 만든 에셋이 조용히 다른 패턴으로 바뀐다.** 구간마다 번호를 비워 뒀다
(턴 흐름 10번대 / 플레이어 반응 20번대 / 적 50번대).

### 폴백은 패턴이 하나도 없을 때만

`EnemyAct`는 `Patterns.Count == 0`인 적에게만 기존 "최전방 1타"를 쓴다.
"인텐트가 없으면 폴백"으로 짜면 **패턴이 전부 쿨다운인 적까지 평타를 때린다**
(자폭 대기 중인 슬라임이 그 사이 계속 공격하던 버그).

---

## 4. 적 인텐트가 동작하는 방식

```
CombatLoop
  ├ DecideEnemyIntents()   ← 라운드 시작. 패턴을 고르고 CurrentIntent에 보관
  ├ PlayerTurn()           ← 플레이어가 예고를 보고 대응
  └ EnemyTurn() → EnemyAct ← 예고한 패턴을 실행. 타겟은 이때 '다시' 계산
```

**패턴만 고정하고 타겟은 실행 순간에 재계산한다.** 이 한 줄이 "슬롯을 옮겨 회피"를 성립시킨다.

`EnemyIntent.previewSlots` / `previewValue` / `kind`는 **화면 표시 전용 스냅샷**이다.
실행 로직이 이걸 읽으면 회피가 깨진다. 슬롯이 바뀌면 `RefreshIntentPreviews()`가 다시 채운다.

---

## 5. 현재 에셋 목록

`Assets/Data/Patterns/`

### 동료
| 에셋 | 소유 | 내용 |
|---|---|---|
| `Pattern_카일의화살` | 궁수 카일 | 턴 시작, 최후열 저격(도발 무시) |
| `Pattern_도란의방벽` | 기사 도란 | 턴 시작, 자기 방어막 6, 쿨 2 |
| `Pattern_리나의치유` | 치유사 리나 | 턴 종료, 최저 HP 아군 회복 5, 쿨 1 |

### 하수인
| 에셋 | 소유 | 내용 |
|---|---|---|
| `Pattern_기본공격` | 고블린·해골병사·돌골렘·저주정령 | 가장 가까운 적 |
| `Pattern_후열저격` | 어둠 궁수 | 최후열, 도발 무시 |
| `Pattern_도발` | 돌 골렘 | 자기 도발, 쿨 2 |
| `Pattern_저주의안개` | 저주 정령 | 전체 취약, 쿨 3 |
| `Pattern_자폭` | 폭발 슬라임 | 1턴 대기 후 전체 피해 + 자멸 |

### 보스
| 에셋 | 소유 | 내용 |
|---|---|---|
| `Pattern_수문장_강타` | 던전 수문장 | 기본 |
| `Pattern_수문장_내려찍기` | 던전 수문장 | 2페이즈, 전열 광역 |
| `Pattern_장군_참격` | 부하 장군 | 기본 |
| `Pattern_장군_회전베기` | 부하 장군 | 전체 광역 6 |
| `Pattern_장군_처형` | 부하 장군 | HP 30% 이하, 최저 HP 20 |
| `Pattern_마왕_암흑탄` | 마왕 | 기본 12 |
| `Pattern_마왕_흑염` | 마왕 | 2페이즈, 전체 8 + 화상 |
| `Pattern_마왕_강림` | 마왕 | 3페이즈, 슬롯4→1 이동 + 자가 도발, 1회 |
| `Pattern_마왕_절멸` | 마왕 | 3페이즈, 24 |

---

## 6. 새 패턴 추가 절차

1. Project 우클릭 → Create → **`Game/Action Pattern`**
2. 파일명은 `Pattern_한글이름.asset` (기존 규칙)
3. 인스펙터에서 `timing` / `targetMode` / `effects` 채우기
   — `effects`는 `Size`를 먼저 지정해야 항목이 나온다
4. 유닛 데이터(`CompanionData` / `MinionData` / `BossData`)의 `patterns` 리스트에 드래그

**코드 수정은 필요 없다.** 새 효과 종류가 필요할 때만 `EffectType`과 `ExecuteEffect`를 건드린다.
