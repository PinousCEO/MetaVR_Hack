# 최종 보고서 — Living AI Partner

- 작업 세션: 2026-11-16 00:53 KST 시작, 09:00 KST 종료 예정이었으나 사용자가 그 뒤(10:50, 11:15경)에 추가 요청을 해서 그 요청분까지 반영함(마지막 갱신 15:45). 이 문서는 세션 중 계속 갱신됨.
- 대상: Meta VR Start Developer Competition 2026, Productivity 출품용 제품.
- 한 줄 결론: **PC 쪽 제품과 포털의 양 프로세스 연동은 실제 Windows 데스크톱에서 동작·측정했다. Quest 실기 검증은 헤드셋이 연결되지 않아 하나도 하지 못했다.** 따라서 "내 캐릭터가 화면 안으로 들어갔다 나온다"는 핵심 경험의 최종 품질(헤드셋으로 본 정합·지연)은 NOT_TESTED이다.

## 1. 무엇이 만들어졌나

| 구성 | 상태 | 산출물 |
|---|---|---|
| Unity 6 공통 프로젝트(공유 캐릭터·포즈·프로토콜·포털 로직). 씬 4개(`Main`, `PcCharacter`, `MrSimulator`, `QuestMR`; 그 밖에 보기 전용 `CharacterGallery`)와 프리팹 6개(`MrCore`, `Partner`, `PortalFx`, `SparkFx`, `TimerPanel`, `ProposalCard`)로 하이어라키에 저작, 모든 참조는 직렬화 필드 | IMPLEMENTED | `MetaVRHack/Assets/LivingPartner/` (`Scenes`, `Prefabs`, `Art`) |
| 캐릭터 "Momo": **사용자가 Tripo로 만든 모델**(노란 보닛과 옷, 얼굴에는 눈만)을 프로젝트 안에서 리깅한 것. 뼈대 비율을 이 모델에 맞추고 자동 스키닝했으며, 눈은 따로 떼어 깜빡이게 했다. 모델 텍스처 위에 2톤 셰이딩과 잉크 외곽선을 입힌다. 화면을 통과할 때는 유리면 앞·뒤를 각 렌더러가 나눠 그리고, 잘린 곳은 단색 절단면으로 막는다. 목의 장식이 작업 상태등이다. 이전의 절차적 디자인 6종과 손 메뉴 Friend 버튼은 제거(이전 파일은 `artifacts/backup-before-tripo-character-2026-11-16/`). UI는 동화책 느낌(크림색 종이 패널, 파스텔 버튼, 영어 문구), 글꼴은 직접 만든 LPStory | IMPLEMENTED, 실행 검증(PC·시뮬레이터). Quest 미실행 | `Art/Characters/Tripo`, `Editor/PartnerModelBuilder.cs`, `Prefabs/Partner.prefab`, `Resources/Shaders/LPPartner.shader`, `LPPartnerCut.shader` |
| Windows 캐릭터 클라이언트(투명·클릭 통과 오버레이, DesktopRoaming / WorkspaceBound / MonitorTransfer, 포털 송수신) | IMPLEMENTED, 실행 검증 | `dist/LivingAIPartner-Windows/` |
| Companion Host(.NET 8 트레이 앱 + 런처, 인증·페어링, 전환 코디네이터, 창/모니터 추적, 창 캡처, 하네스, 상태 저장) | IMPLEMENTED, 실행 검증 | `dist/LivingAIPartner-Host/` (`lpctl.exe` 포함) |
| Quest 클라이언트(OpenXR, 패스스루, 손 추적, 카메라 프레임 관찰, 페어링 UI, 보정 절차) | IMPLEMENTED, **실기 미실행** | `dist/LivingAIPartner-Quest/LivingAIPartner.apk` (53MB, 14:41 최종 재빌드 — 사용자의 Tripo 캐릭터·직접 만든 글꼴·동화풍 디자인·영어 UI·자연스러운 비 반영. 이 빌드의 매니페스트는 다시 검사하지 않았다. 매니페스트 검사는 11:07 빌드에서 마지막으로 했고 그 뒤 매니페스트에 영향을 주는 변경은 없음) |
| 데스크톱 MR 시뮬레이터(검증 도구, Quest 아님) | IMPLEMENTED | Windows 빌드에 포함(`-lp-role mrsim`) |
| 단위 테스트 | 306개 통과 | `Companion/LivingPartner.Tests` |
| 심사자용 샘플 프로젝트(로컬 git 원격, 개인 키 불필요) | IMPLEMENTED | `samples/sample-site`, `tools/Setup-Sample.ps1` |

실행: `.\Start-LivingPartner.ps1` (PC) → 헤드셋에 APK 설치 → 페어링 → 보정. 상세는 `RUNBOOK.md`.

## 2. 핵심(P0) — 지정 위치를 통과하는 포털

구현 방식
- 통과 계획(`CrossingPlan`)을 **선택한 화면 지점 기준 좌표계**(화면 표면이 정확히 z=0)로 정의하고, Quest와 PC가 같은 계획을 같은 공유 시계로 평가한다. 같은 메시·같은 포즈를 화면 평면으로 잘라 **상보적인 신체 부분**만 각자 그린다. 교체·페이드·가림 효과는 쓰지 않는다(얇은 링은 경계 표시용).
- 진입은 비스듬한 다이빙(37~49°)이라 정면에서 볼 때 방 안에 남은 다리와 화면 안의 상체가 한 선으로 이어져 보인다. 손/머리 → 몸통 → 다리 → 발 순서로 지나가며, 마지막 표본점이 통과한 뒤에만 소유권이 넘어간다.
- 높이·거리·하단 여유에 따라 DiveFlip / RunLeap / BezelClimb 변형이 실제 기하로 선택된다. 접근은 사용자 응시 → 목적지 응시 → 회전 → 걷기/달리기 → 준비 자세.
- PC는 헤드셋이 보내는 머리 위치로 off-axis 투영을 해서 화면 안쪽 절반을 사용자 시점에 맞춘다(평면 모니터의 한계상 한 시점의 정합이며 입체 연속성은 아님).
- 상태: Idle → TargetSelected → Approaching → ReceiverReady → Crossing → Settling → Complete, 실패 경로 Hold / Recovering / Cancelled. 메시지는 멱등.

실제로 확인한 것(WINDOWS_BUILD, MR 쪽은 시뮬레이터)
- 전환 200건 중 194건 완료(MR→PC 100, PC→MR 94), 두 모니터(세로·음수 좌표 포함), 서로 다른 52개 지점, 창 고정 103 / 화면 고정 97, 변형 RunLeap 104 / DiveFlip 93 / BezelClimb 3. 미완료 6건은 수정 전 결함 3건(접근 시간 초과)과 의도한 강제 종료 테스트 3건(모두 기대대로 MR에 온전히 복귀). 이 중 107건(105 완료)은 새 캐릭터로, 90건(88 완료)은 씬/프리팹 구조 빌드로 실행. (`artifacts/transition-stats.txt`)
- 공유 시계 오차(슬로모션·장시간 정지 7건을 뺀 완료 187건): PC 최대 6.8ms(평균 0.93), MR 시뮬레이터 최대 3.6ms(평균 0.88). 6.8ms는 복구 테스트가 접근 중에 대상 창을 옮기던 전환 1건(04:45)에서 나온 값이고, 나머지는 모두 PC 2.4ms 이하. PC 표시 지연 32ms는 계획 시각을 앞당겨 보정.
- 씬/프리팹 구조로 바꾼 뒤 재검증: 단위 테스트 280/280, 포털 반복 10/10·6/6·6/6·6/6(마지막은 최종 빌드), 복구 A~E 전부 통과(2회), MR 기능(비 차폐·손 탑승·타이머·핀치 선택) 통과, 창 따라가기(이동·최소화·복원) 통과, 최종 빌드 무편집 왕복 영상과 1/4 속도 진입 영상 녹화.
- 50% 정지 화면에서 PC 실화면에 상체+절단면, 반대 방향에서는 하체만 남는 것을 캡처로 확인. 새 캐릭터·최종 빌드 기준 캡처는 `artifacts/half-through-enter-pc.png`·`-enter-mr.png`·`-exit-pc.png`·`-exit-mr.png`(같은 순간의 PC 실화면과 시뮬레이터 MR 화면: 서로 반대쪽 절반만 그려짐), 이전 캐릭터 기준은 `enter50-pc.png`, `exit50-pc.png`.
- 복구: 이동 중 창 이동(포털 재고정), 최소화(Hold→복원 후 재개), MR 단절(Hold→재접속 후 1회만 완료), 통과 중 MR 상실(PC 단독 완료), 통과 중 PC 상실(되감기 후 MR에 온전히 복귀).

확인하지 못한 것
- 헤드셋으로 본 두 절반의 정합 오차(mm), 지연, 손 지목 정확도, 터치 보정 정확도. 전부 NOT_TESTED(BLOCKED_EXTERNAL).

## 3. 그 밖의 기능

| 기능 | 결과 |
|---|---|
| 투명 데스크톱 앱 / 클릭 비방해 | PASS (실화면 캡처, 히트 테스트가 아래 창으로 통과, 포커스 미획득). 최종 배포본(`dist\Start-LivingPartner.ps1`)으로 04:29에 다시 확인 |
| 창 이동·리사이즈 추종, 최소화 시 숨김, 복원, 다른 창에 가려짐 | PASS (실측) |
| PC 내 모니터 간 이동(별도 상태), 다른 모니터에서의 출구 | 좌우 인접 PASS, 다른 모니터 출구 PASS. 상하 인접은 04:33에 처음 녹화해 보니 전환 순간 한 프레임 동안 몸이 엉뚱한 위치에 그려지는 결함이 있었고, 수정 후 04:38 재녹화에서 양방향 PASS(경계를 끊김 없이 통과) |
| 전체화면 앱 감지 시 일시 중단 | PASS(04:55, 게임이 더 이상 실행 중이 아닐 때 화면을 덮는 테두리 없는 창으로 확인: 숨김 + 사유 보고 → 창이 닫히면 같은 자리에 복귀). 독점 전체화면 게임으로는 확인하지 않음 |
| 보안 데스크톱(UAC·잠금 화면)에서 일시 중단 | 구현됨, **NOT_TESTED** |
| 실제 분석(창 캡처 + Claude Code) → 제안 4개(근거 file:line, 추정 표시) | PASS (실 하네스, 38초, $0.449) |
| 제안 선택 → 전용 브랜치/워크트리 수정 → 테스트 → 승인 후 푸시 | PASS (로컬 bare 원격). GitHub는 미인증으로 NOT_TESTED |
| 두 번째 실 하네스: Codex CLI 어댑터(`--harness codex`)로 같은 파이프라인 | 3번째 시도에 PASS — 분석 30초·제안 3개(소스 확인됨) → `styles.css` 수정(커밋 `cf9ecf0`) → 테스트 통과 → 승인 후 로컬 원격에 작업 브랜치만 푸시. 앞의 두 번은 실패(한글 프롬프트가 UTF-8로 전달되지 않음 / 프롬프트가 셸 사용을 금지해 Codex가 파일을 못 고침)였고, 둘 다 `TaskFailed`로 그대로 표시됨. 수정 후 재실행 |
| 타이머 모듈(자연어 생성·재실행·일시정지·재개·초기화·시간/표시 변경·완료 알림·재시작 후 복원) | PASS |
| 최소화 중 분석 완료 시 복귀 정책(숨김 유지 + 안내, 호출 시 복귀) | PASS(표시된 테스트 더블 사용) |
| PC 캐릭터 말풍선, 클라이언트 시작 시 포커스 미탈취 | PASS |
| 손 탑승 / 가상 비 차폐 | 시뮬레이터의 스크립트 손으로 PASS, 실제 손은 NOT_TESTED |
| 카메라 기반 환경 관찰 | 구현만 됨, NOT_TESTED |
| 모니터 보정 절차(모서리 4점+책상), PC 모서리 마커 | 시뮬레이터 스크립트 손끝으로 PASS(마커가 실제 픽셀 모서리에 정확히 위치, 저장·건너뛰기·종료 정상). 실제 손끝 정확도는 NOT_TESTED |
| 관찰자 머리 위치 기반 투영 | 시뮬레이터에서 PASS(머리를 ±0.4m 옮기면 PC 쪽 절반이 같은 방향으로 +32/−26px 이동) |
| 지목 → 핀치(미리보기) → 다시 핀치(확정) 흐름, 발화 구간 지목 이력 연결 | 시뮬레이터 스크립트 손 + 텍스트 발화로 PASS(손을 내린 뒤 0.6초 늦은 발화도 지목 픽셀 정확히 복원) |
| 첫 실행 페어링(손 숫자패드) | 시뮬레이터를 토큰 없이 띄워 스크립트 손끝으로 버튼을 눌러 PASS — 틀린 코드는 거부, 호스트의 코드 입력 후 접속. 이 세션에서 숫자패드가 실제로 실행된 것은 이것이 처음. 실제 손·LAN 경로는 NOT_TESTED |
| 에디터에서 Play(`MrSimulator.unity`) | PASS — 호스트에 접속해 캐릭터·패널 표시, 플레이 중 오류 없음 |
| 음성 입력 | **미구현**(손 조작과 PC 텍스트 입력으로 대체) |

### 3.1 16:10~17:20에 추가한 두 가지 (사용자 요청)

**(1) 가리킨 화면 리뷰 + 수정본, 파일 읽기** — 코드 프로젝트가 연결되지 않은 창은 이제 OpenAI 키로 직접 봅니다.

| 항목 | 결과 |
|---|---|
| 가리킨 창을 인식해 들어가서 보고 피드백 | PASS(시뮬레이터 + 실제 OpenAI 호출). 요청 후 49초(통과 이동 포함)에 메모 5개. 일부러 넣은 결함(제목 오타 "Creat your acount", 흐린 글씨, 안 보이는 입력칸, "Sumbit", 크기가 다른 버튼)을 짚음. 핵심은 즉시 말로 전달 |
| 실제 수정본 파일 | PASS. PC의 `문서\LivingAIPartner\Reviews\`에 `feedback.png`(번호 표시본), `revised.png`(이미지 모델이 고쳐 그린 수정본), `review.pdf`(요약·표시본·피드백·수정본·원본 3쪽) 저장 |
| MR로 가지고 나오기 | PASS(시뮬레이터). 메모 카드 5장 + "저장됨" 패널, **Open/Preview**로 PC에서 PDF 열기 |
| PDF 읽기 | PASS. 16초. 사실(예산 4,200,000원, 출시일 2026-12-03)과 심어둔 모순(출시 1주 전 회의가 12-10)을 찾음. `notes.pdf` + 수정본 `revised.pdf` 저장 |
| 읽은 파일에 대한 후속 질문 | PASS. "마케팅 예산은?" → "1,500,000 KRW". 한국어로도 1회 확인("출시일이 언제야?" → "2026-12-03이야.") |
| 창 제목으로 파일 찾기 | PASS("meeting-notes.pdf - Sample Viewer" 창 → 문서 폴더의 같은 이름 파일) |
| 그림 파일(PNG) 읽기 | PASS(호스트 단독, 35초, 화면 리뷰와 같은 3개 파일) |

한계: 수정본 그림은 이미지 모델이 다시 그린 시안입니다. 위 실행에서 제목과 버튼 오타는 고쳤지만 라벨 오타 두 개는 그대로 남겼고, 비율도 원본과 조금 다릅니다. 표시 위치는 대략적입니다. PDF는 쪽을 그림으로 넣은 형식이라 글자 선택이 안 됩니다. 탐색기 선택, 트레이 메뉴, 헤드셋 없이 자동으로 문서 열기, 큰 PDF는 실행해 보지 않았습니다. Quest에서는 전부 NOT_TESTED.

**(2) 모니터를 사이에 둔 테니스** — 모니터 유리가 네트입니다. 캐릭터는 모니터 안의 코트(모니터 전체를 머리 위치 기준 원근으로 그린 상자)에서 치고, 공이 유리를 통과해 방으로 나오면 사용자가 편 손바닥으로 받아칩니다. 공 위치는 전송하지 않고, 타구(위치·속도·시각)만 보내 PC와 헤드셋이 같은 시계로 같은 궤적을 계산해 각자 유리의 자기 쪽만 그립니다.

| 항목 | 결과 |
|---|---|
| 방에서 시작 → 캐릭터가 스스로 모니터로 들어가 경기 시작 | PASS(시뮬레이터) |
| 랠리 | PASS. 양쪽 타구가 번갈아 이어짐(40초에 손 12타·캐릭터 15타, 최장 17타). 캐릭터는 가끔 놓침 |
| 득점·5점 종료·코트 닫힘, 중간 정지, 이후 책상으로 복귀 | PASS |
| 공 궤적 단위 테스트 | 9개 통과(유리 구멍 통과, 프레임 충돌, 바운드, 연속성, 순수 함수) |

한계: 사용자 손은 시뮬레이터가 공 경로에 놓아주는 스크립트 손입니다. 실제 손으로 맞는지, 공이 실제 유리를 지날 때 하나로 보이는지는 헤드셋 없이는 알 수 없습니다(NOT_TESTED). 소리는 없습니다.
## 4. 요구사항 대비 추가한 것과 이유

- 데스크톱 MR 시뮬레이터: 헤드셋 없이 두 프로세스 연동을 실행·측정하기 위해. 결과는 `simulated`로 표시.
- `lpctl`: 포털 디버그(정지/느린 재생/되감기/반복), 상태 진단, 자동 검증.
- PC 측 지연 보정, 시점 기반 바닥 보정: 정합 품질을 위해.
- 전체화면 앱 감지: 사용자 작업 방해 방지.
- `link.xml`: IL2CPP 스트리핑에서 프로토콜 DTO 보존.
- 에디터 저작 도구 `LPSceneBuilder`(Unity MCP 또는 메뉴로 실행): 에셋·프리팹·씬을 만들고 직렬화 참조를 연결한 뒤, 비어 있는 참조가 없는지 검사한다.

### 4.1 세션 중 사용자 피드백으로 바꾼 것

| 피드백 | 조치 |
|---|---|
| 캐릭터가 별로다 → 툰·캐주얼·단순, 눈만 | 캐릭터 전면 교체: 둥근 머리+작은 귀, 한 덩어리 몸, 눈만 있는 얼굴, 2톤 툰 셰이더와 외곽선. 포털 통과 단위 테스트는 새 비율로 다시 통과 |
| 전체 디자인을 모션그래픽처럼 화려하게 | 팔레트 통일, 그라디언트 글래스 패널·알약 버튼(등장 팝·호버 반응), 회전하는 그라디언트/점선 링·물결·반짝임으로 된 포털, 이착지 스파크, 방사형 타이머 링. 효과는 개구부 **둘레**에만 그려 통과 중인 몸을 가리지 않는다(원 지시의 금지 사항 유지) |
| Unity MCP에 맞게 하이어라키에 넣어라(런타임 생성·Find 우려) | 런타임 생성 구조를 전부 걷어냄. 모든 오브젝트를 씬·프리팹으로 옮기고 `[SerializeField]` 참조로 연결. 플레이어에 컴파일되는 코드에 `GameObject.Find` / `transform.Find` / `GetComponent` / `new GameObject` / `AddComponent` / `Instantiate` / `Resources.Load` 없음(검사 결과 일치 1건은 `#if UNITY_EDITOR` 안의 에디터 전용 코드, `artifacts/no-runtime-lookups.txt`). 개수가 변하는 것(모니터·타이머·제안 카드)은 하이어라키에 미리 둔 풀 사용. 에디터에서 `MrSimulator.unity`를 열면 모든 오브젝트가 하이어라키에 보이고 Play도 된다 |

| 물 같은 효과도 툰 스타일의 화려한 셰이더로, 손으로 막으면 효과가 손 위에 뜨게 | 비 효과를 새로 만듦. `LPFx` 셰이더에 툰 물 모드 4개(빗방울·튀김 왕관·물막·물방울: 단색 면 + 잉크 외곽선 + 흰 하이라이트) 추가. 빗방울마다 실제로 닿는 곳(비를 향한 손 윗면 → 캐릭터 머리 → 책상)을 계산해 그 표면 위에 튀김을 그린다. 손에 맞은 튀김은 손을 따라 움직이고, 비를 계속 받는 손에는 일렁이는 물막이 생기며 가장자리에서 물방울이 떨어진다. 오브젝트는 `MrCore/Rain/` 아래 하이어라키에 있고(`WaterFx`) 런타임 생성 없음. 시뮬레이터의 스크립트 손으로 확인(`artifacts/toon-rain-hand-shelter.png`, `toon-rain-exposed.png`). 실제 손 위에서 어떻게 보이는지는 헤드셋이 없어 NOT_TESTED |

| (10:50, 캡처를 본 뒤) 빗방울이 너무 굵다 / UI는 영어여야 한다 / 디자인을 심플하지만 아름다운 동화 같은 느낌으로 / 빗방울은 자연스럽고 실사 같은 느낌으로 | 앞의 두 방향(화려한 모션그래픽, 툰 물)을 접고 다시 만듦. **UI 전부 영어**: 패널·버튼·말풍선·캐릭터가 하는 말(호스트 메시지 포함), 분석 결과도 영어로 요청. 한국어는 입력 문장으로만 계속 인식. **디자인**: 크림색 종이 패널에 가는 갈색 테두리와 부드러운 그림자, 파스텔 알약 버튼, 갈색 잉크 글씨, 꿀색 포털 링. 네온 그라디언트·글로우·움직이는 광택 제거(`Ui.Pal`, `LPFx` 패널 모드). **비**: 가늘고 부드러운 빗줄기(굵기 약 3mm, 길이와 진하기가 조금씩 다름), 닿는 곳의 잔물결, 작은 투명 물방울, 손 위의 거의 투명한 물막. 손 위에 뜨는 동작은 그대로. 캡처: `artifacts/storybook-rain-hand-shelter.png`, `storybook-rain-exposed.png`. "동화 같은 느낌"을 이렇게 해석한 것은 내 판단이다. 이 시점의 글꼴은 Unity 기본 글꼴이었고, 다음 행의 요청으로 직접 만든 글꼴로 바꿨다 |
| (11:15경) 글꼴은 직접 만들어야 한다 / 디자인은 어느 정도 마음에 든다 / 캐릭터를 여러 디자인의 3D로 만들어 달라 | **글꼴**: 둥근 펜으로 쓴 듯한 영문 글꼴 "LPStory"를 직접 설계. 글자마다 선과 타원 호(펜의 중심선)로 정의하고 에디터 도구 `StoryFontBuilder`가 두 굵기(Regular·Bold)로 아틀라스에 그려 Unity 폰트 에셋으로 저장한다. ASCII 전체와 `· • … – — ‘ ’ “ ” → × ° ✓`, 108자. 숫자는 폭이 같아 타이머가 흔들리지 않고, 대문자 I에는 가로획을 넣어 "AI"가 "Al"로 읽히지 않게 했다. 모든 글자 오브젝트는 `StoryLabel` 컴포넌트를 거치며, 이 글꼴에 없는 글자(한글 창 제목 등)가 오면 그 라벨만 시스템 글꼴로 바꿔 표시한다. 글자 폭을 실제로 재서 말풍선 크기와 패널 줄바꿈을 맞춘다(전에는 글자 수 어림). **캐릭터**: 같은 뼈대·같은 동작을 쓰는 3D 디자인 6종 — 곰(기존 Momo), 토끼(긴 귀·솜꼬리), 고양이(뾰족 귀·굽은 꼬리), 여우(귀 끝·발이 짙은 색, 크림색 주둥이·배, 풍성한 꼬리), 새싹(머리 위 잎 두 장), 펭귄(흰 얼굴·배, 주황 발). 모두 눈만 있는 얼굴 유지. 디자인은 호스트 상태에 저장되어 PC와 MR이 항상 같은 모습을 보이고, 포털 통과도 그대로 동작한다. 캡처: `artifacts/character-gallery.png`, `font-specimen.png`, `pc-say-bubble.png`, `half-through-fox-*.png`. 유료 Studio 3D 생성 도구는 예산 승인이 없어 쓰지 않았고, 전부 프로젝트 안의 코드로 만들었다. 헤드셋에서 어떻게 보이는지는 NOT_TESTED |
| (12:15~13:20) 레퍼런스 이미지를 여러 차례 요청했으나 모두 마음에 들지 않음. (13:55경) 직접 Tripo로 만든 모델을 `Character_Reference`에 넣었으니 그것으로 다시 작업하라 / 포털에 머리가 들어갔으면 머리는 컴퓨터 안에, 나머지 몸은 바깥에 — 이 렌더링이 전부 되어야 한다 | 그 모델을 파트너로 교체. 뼈대 없는 T포즈 메시여서 뼈대 비율을 다시 재고 자동 스키닝 도구(`PartnerModelBuilder`)를 만들었다. 통과 중 25·50·75% 지점에서 멈춰 양쪽 화면을 캡처해, PC에는 유리면 뒤쪽만, 방 쪽에는 앞쪽만 그려지고 잘린 면이 단색으로 막히는 것을 확인(`artifacts/tripo-half-through-sheet.png`). 무편집 왕복 녹화 `tripo-portal-roundtrip.mp4`. 단위 테스트 283개, 통과 반복 10/10, MR 기능 점검 4/4 통과. Quest APK는 다시 빌드했고 설치·실행은 하지 못함. 이 빌드에서 복구·창 추종·페어링·하네스 점검은 반복하지 않았다 |
| (14:48경) 애니메이션을 다듬고 어색한 부분을 해결 / AI 시스템이 잘 들어갔는지 확인하고 OpenAI 키로 작업 / 가리키는 방향 인식, 음성 채팅, 꼬집어서 대화하기까지 전부 확인 | **AI**: 그때까지 있던 것은 코드 분석용 하네스와 키워드 해석뿐이었고 대화·음성은 없었다. OpenAI 키(이 Windows 계정으로만 풀리게 암호화해 저장소 밖에 저장)로 음성 인식 → 답변과 동작 하나 → 음성 합성을 새로 만들었다. 캐릭터를 꼬집거나(멀리서 가리키고 꼬집어도 됨) 손 메뉴 Talk를 누르면 듣기 시작하고, 말이 끝나면 스스로 멈춘 뒤 말풍선과 목소리로 답한다. **점검**: 음성 5/5(녹음 파일을 마이크 경로로 주입: 영어 명령→타이머, 한국어 대화, 가리키며 말하기→가리킨 픽셀에서 0.2px, PC 안에서 돌아오기, 꼬집기→실제 마이크 열림), 포인팅 27/27지점과 실제 진입 2회 0.0px, 단위 테스트 297개, 회귀 점검 통과. **애니메이션**: 서 있을 때 숨쉬기·무게 이동, 걷기·달리기의 뒤뚱거림과 탄력, 제자리 회전 시 발 떼기, 점프의 눌림·늘어남, 듣기·말하기 동작, 통과 뒤 공중제비 제거. **하지 못한 것**: 실제 사람 목소리로 마이크에 말해 보는 시험, Quest에서의 모든 것(마이크 권한, 실제 손으로 꼬집기 인식, 실제 손의 가리키기 정확도). 애니메이션은 정지 프레임과 녹화 한 번으로만 판단했다 |

## 5. 정직하게 남기는 실패와 한계

- 세션 중 발견·수정한 결함 22건은 `TEST_RESULTS.md`에 기록(숨은 창 렌더링 누락, 틸트 모니터 재돌출, PC 상실 시 되감기 누락, 하네스 프롬프트 인코딩, 탑승 중인 손이 버튼을 누르는 오작동 등).
- 검증 스크립트 자체의 오류로 잘못된 FAIL이 두 번 나왔다(비 테스트: MR 클라이언트 접속 전에 명령 전송 / 명령 이름 오류). 제품 코드는 그 때문에 바꾸지 않았고, 스크립트를 고쳐 다시 돌린 결과만 PASS로 적었다.
- Quest 쪽 코드는 한 번도 기기에서 실행되지 않았다. 손바닥 법선 부호, 핀치 임계값, 지목 광선, 패스스루 구성, 비콘 수신은 첫 실행에서 조정이 필요할 가능성이 높다.
- 캐릭터 모델은 사용자가 Tripo로 생성한 것이고(라이선스는 사용자 확인 사항), 뼈대·스키닝·동작은 이 프로젝트에서 자동·절차적으로 만든 것이다. 팔이 짧고 관절이 16개뿐이라 동작이 단순하며, 포즈는 정지 렌더를 보고 맞췄다. 전문 아티스트의 리깅·모션캡처 수준은 아니다.
- 직접 만든 글꼴은 영문·숫자·기호만 있다. 한글은 시스템 글꼴로 대신 표시되므로 한 화면에 두 글꼴이 섞일 수 있다. 헤드셋에서의 가독성은 확인하지 못했다.
- 글꼴과 줄바꿈을 바꾼 뒤 페어링 숫자판·작업 패널·제안 카드 화면은 다시 띄워 보지 않았다(복구·창 추종·하네스 점검도 이 빌드에서는 반복하지 않음).
- 캐릭터 메시를 고쳤는데 에셋에는 이전 정점이 남아 펭귄 얼굴에 줄무늬가 생기는 결함이 있었다. 첫 캡처를 보고 찾았으며(Unity의 복사 함수가 정점 수가 달라지면 정점 데이터를 갱신하지 않음) 메시 전용 복사와 검증을 넣어 고쳤다.
- 처음에는 Unity 오브젝트를 전부 런타임 코드로 만들었다. 사용자 지적을 받고 씬·프리팹 구조로 다시 만들었으며, Quest 씬(`QuestMR.unity`)의 XR Origin 구성은 여전히 기기에서 실행해 보지 못했다.
- 공식 규정 페이지는 자동 접근이 차단되어 읽지 못했다. Meta 공지로 확인한 범위는 `reports/SUBMISSION_CHECKLIST.md`.

## 6. 외부 블로커

`BLOCKERS.md` 참조: (B1) Quest 미연결, (B2) 공식 규정 미확인, (B3) GitHub 미인증, (B4) 음성 대화는 구현했으나 Quest에서는 미검증.

## 7. 다음 작업

`NEXT_STEPS.md` 1절의 "헤드셋 첫 1시간" 점검이 최우선이다. 이후 지연 보정값 측정, 지목/보정 사용성, 음성 입력 순.

## 8. 증거 파일

`artifacts/` — 화면 캡처(`gate1-*`, `enter50-pc`, `exit50-pc`, `gate3-*`, `pc-bound-portrait-monitor`, `mr-sim-modules`, `bezel-climb-*`), `portal-roundtrip-unedited.mp4`(시뮬레이터 MR 화면 + 실제 PC 창, 무편집), `transitions.jsonl`, `portal-loop.txt`, `recovery-tests.txt`, `gate3-window-tests.txt`, `harness-e2e.txt`, `tasks.jsonl`, `sample-change-task-ff4d4e16.diff`, `feature-tests.txt`, `quest-apk-badging.txt`, `build-*.txt`.

03:26 이전 캡처에는 **이전 캐릭터와 UI**가 찍혀 있다(투명도·절단·z순서 증거로는 유효). 현재 모습과 씬/프리팹 구조에 대한 증거는 다음이다: `scene-build.txt`(참조 검사), `hierarchy.txt`(하이어라키 덤프), `hierarchy-mrsimulator-editor.png`(에디터 편집 모드 캡처), `no-runtime-lookups.txt`(플레이어 코드 검사), `hierarchy-pc-character.png`, `hierarchy-sim-window.png`, `hierarchy-sim-working.png`, `portal-roundtrip-final-build.mp4`(최종 빌드 무편집 왕복), `portal-loop-new-look.txt`, `portal-loop-hierarchy.txt`, `portal-loop-final-build.txt`, `recovery-tests-hierarchy.txt`, `mr-feature-checks.txt`, `harness-e2e-codex.txt`, `sample-change-codex-task-13124526.diff`, `transition-stats.txt`, `portal-roundtrip-final-build-sheet.png`(영상의 3초 간격 장면 모음), `shipped-copy-smoke.txt`·`shipped-copy-pc-character.png`(dist 배포본을 실행 안내대로 띄운 확인), `half-through-*`(50% 정지 시 양쪽 화면), `portal-entry-slowmo-quarter-speed.mp4`·`portal-entry-slowmo-sheet.png`(호스트 시계를 1/4로 늦춘 진입, 영상 속도 조작 아님), `pairing-flow.txt`·`pairing-numpad.png`, `monitor-transfer-stacked-*`·`monitor-transfer-side-*`(모니터 간 이동 녹화), `sim-ui-*.png`(MR UI 상태), `editor-playmode-mrsimulator.png`, `window-follow-checks.txt`, `fullscreen-suspend-check.txt`, `bezel-climb-new-character.mp4`·`-sheet.png`, `toon-rain-hand-shelter.png`·`toon-rain-exposed.png`(이전 툰 물 효과), `storybook-rain-hand-shelter.png`·`storybook-rain-exposed.png`(동화풍 UI·영어·자연스러운 비), `character-gallery.png`(캐릭터 6종)·`font-specimen.png`(직접 만든 글꼴 견본)·`design-checks.txt`·`design-*-mr.png`·`half-through-fox-*.png`·`pc-say-bubble.png`·`sim-ui-caption-fallback.png`·`portal-loop-designs-font-build.txt`(현재 모습: 캐릭터 6종과 새 글꼴). 11:50 이전 캡처의 글자는 Unity 기본 글꼴이다. 11:00 이전의 다른 캡처·영상은 이전 디자인(네온 색·한국어 UI)으로 찍힌 것이다.

14:30 이후의 현재 캐릭터(사용자의 Tripo 모델)에 대한 증거: `partner-pose-sheet.png`(모든 포즈), `partner-cut-sheet.png`(에디터에서 화면에 잘린 모습), `tripo-half-through-sheet.png`와 `tripo-half-through-*`(실행 중 25·50·75% 정지, PC와 시뮬레이터 양쪽), `tripo-portal-roundtrip.mp4`·`tripo-portal-entry-sheet.png`(무편집 왕복과 진입 장면), `tripo-pc-standing.png`, `tripo-ui/`, `partner-status-light.png`, `design-checks-tripo.txt`, `portal-loop-tripo.txt`, `mr-feature-checks-tripo.txt`. 그 이전의 캡처·영상에는 예전 캐릭터가 찍혀 있다. 생성 모델(Tripo)의 라이선스와 AI 생성 에셋 공개 규정은 확인하지 않았고 제출 점검표에 넣었다.

15:45 기준 음성·포인팅·애니메이션에 대한 증거: `voice-checks.txt`, `pointing-checks.txt`, `voice-listening-mr.png`·`voice-speaking-mr.png`(시뮬레이터), `voice-reply-sample.wav`(캐릭터 목소리 예시), `partner-motion-sheet.png`·`partner-pose-sheet-v2.png`, `mr-feature-checks-voice-build.txt`, `portal-loop-voice-build.txt`, `design-checks-voice-build.txt`, `quest-apk-badging-voice-build.txt`. 14:30~15:10의 통과 캡처·영상(`tripo-*`)은 공중제비를 없애기 전의 동작이다.

17:20 기준 추가 증거: `review-checks.txt`, `review-window.pdf`·`review-window-feedback.png`·`review-window-revised.png`(가리킨 창 리뷰 결과물), `read-sample-notes.pdf`·`read-sample-revised.pdf`·`read-sample-notes-ko.pdf`(PDF 읽기 결과물), `review-mr-cards.png`(MR 카드), `tennis-checks.txt`, `tennis-pc-court.mp4`·`tennis-pc-court-2.png`(PC 코트), `tennis-mr-sim.png`(방 쪽), `partner-tennis-sheet.png`(스윙 포즈).

## 9. 저장소 상태

커밋·푸시는 하지 않았다(요청 범위 밖). 작업 트리 전체가 미커밋 상태로 남아 있으며, `.gitignore`가 Unity 캐시·`dist/`·토큰을 제외한다.
