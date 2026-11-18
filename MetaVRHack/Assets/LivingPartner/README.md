# LivingPartner — Unity 프로젝트 구조

모든 오브젝트는 씬과 프리팹에 들어 있고, 스크립트는 `[SerializeField]` 참조로만 오브젝트에 접근한다.
플레이어 코드에는 `GameObject.Find` / `transform.Find` / `GetComponent` / `new GameObject` / `AddComponent` / `Instantiate` / `Resources.Load`가 없다.

## 씬 (`Scenes/`)

| 씬 | 용도 | 루트 오브젝트 |
|---|---|---|
| `Main.unity` | Windows 빌드의 시작 씬. `-lp-role`에 따라 아래 두 씬 중 하나를 연다 | `LivingPartner [AppBootstrap]` |
| `PcCharacter.unity` | 투명 데스크톱 캐릭터 클라이언트 | `PcCharacter [PcApp]` → `OverlayCamera`, `Partner`, `Portal/`, `Status/`, `Caption/`, `Debug/` |
| `MrSimulator.unity` | 헤드셋 없이 MR 쪽을 돌려 보는 데스크톱 시뮬레이터(검증 도구) | `MrSimulator [MrSim]` → `SimHead`, `SimRoom/` + `MrCore` |
| `QuestMR.unity` | Quest 빌드의 유일한 씬 | `QuestMR [QuestApp]`, `XR Origin/Camera Offset/Main Camera`, `AR Session`, `MrCore` |
| `CharacterGallery.unity` | 빌드에 들어가지 않는 보기 전용 씬. 캐릭터 디자인 6종과 글꼴 견본 | `CharacterGallery` → `GalleryCamera`, `Partner …` 6개, `FontSpecimen/`(`SpecimenCamera`) |

에디터에서 바로 보려면 `MrSimulator.unity`를 열면 된다. Companion Host가 켜져 있으면(`tools\Start-Dev.ps1 -LoopbackOnly`) Play도 된다(호스트 접속·캐릭터·패널 표시까지 확인함. Game 뷰 마우스를 가리키는 손으로 쓰는 입력은 코드만 넣었고 아직 직접 눌러 보며 확인하지는 못했다).

## 프리팹 (`Prefabs/`)

| 프리팹 | 내용 |
|---|---|
| `MrCore` | MR 쪽 전체. `MrApp` + `Partner`, `Portal/`, `Pointing/`, `Captions/`, `Monitors/`(풀 6), `Rain/`(`Drops/` 빗줄기 110, `Water/` 잔물결 40·물방울 60·반짝임 10·손별 앵커와 물막), `Panels/`(HandMenu·Pairing·Calibration·TaskPanel), `Timers/`(풀 4), `Cards/`(풀 5) |
| `Partner` | 캐릭터 Momo(Tripo로 만든 모델을 리깅한 것). `Visual/Root/…` 본 16개, 스킨 메시 `Body`, 눈 2개(`EyeL`, `EyeR`), `ContactShadow`, `CutFace`(화면에 잘린 단면을 덮는 쿼드). `PartnerRig`의 `Designs`에 `Art/Characters/Design_momo.asset`이 연결되어 있다 |
| `PortalFx`, `SparkFx` | 포털 링·물결·반짝임, 이착지 스파크(쿼드 풀) |
| `TimerPanel`, `ProposalCard` | 타이머 패널, 제안 카드(`MrCore` 안에서 풀로 사용) |

## 컴포넌트 한눈에 보기

- `FxQuad` — 모션그래픽 쿼드 하나(링, 패널, 반짝임, 그림자…). 인스펙터에서 `mode / color / color2 / param / order`를 바꾸면 에디터에서도 바로 보인다. 머티리얼은 `Art/Materials/LPFx` 하나를 공유하고 값은 MaterialPropertyBlock으로 넣는다.
- `WaterFx` — 비의 물 효과(자연스러운 표현). 빗방울이 닿은 표면(손 위·머리·책상)에 잔물결을 띄우고, 비를 계속 받는 손 위에는 얇은 물막과 가장자리 물방울을 그린다. 셰이더는 `LPFx`의 모드 9~12(빗줄기·잔물결·물막·물방울).
- `Ui.Pal` — 전체 색상표(동화책 느낌: 크림색 종이, 갈색 잉크, 파스텔). 색을 바꾸려면 여기를 고치고 저작 메뉴를 다시 돌린다. 화면에 보이는 글자는 모두 영어다.
- `UiPanel`, `UiButton` — 공간 패널과 알약 버튼. 버튼이 하는 일만 코드(`MR/MrUi.cs`의 `InitUi`)에서 연결한다.
- `HandMenuView`, `PairingView`, `CalibrationView`, `TaskPanelView`, `TimerPanelView`, `ProposalCardView` — 패널별 버튼 참조 묶음.
- `MonView` — 보정된 실제 모니터 하나(외곽선 포함). 풀에서 켜고 끈다.
- `PartnerRig` — 본·메시·눈·그림자·절단면 참조와 포즈 적용. `SetClip(…)`으로 화면 유리면 한쪽만 그리게 하고, 그동안 `CutFace`를 유리면 위에 놓는다.
- `PartnerDesign` — 디자인 하나(ScriptableObject): id, 이름, 몸 메시, 눈 위치. 지금은 `momo` 하나다. 어떤 모습인지는 호스트 상태(`CharacterInfo.design`)가 정하므로 PC와 MR이 항상 같다.
- `VoiceIo` — 캐릭터의 귀와 목소리(`Partner` 프리팹의 `Head/Voice`, `AudioSource`와 함께). 말 걸기 요청이 있을 때만 마이크를 열어 한 문장을 녹음하고(조용해지면 스스로 멈춤), PC가 보내 준 음성을 재생한다. MR 쪽 연결은 `MR/MrVoice.cs`(캐릭터를 꼬집거나 손 메뉴 **Talk**), PC 쪽은 `PcApp`(트레이의 Talk).
- `StoryLabel` — 글자 오브젝트 하나(`TextMesh` 옆에 붙는다). 직접 만든 글꼴 **LPStory**(`Art/Fonts/`, Regular·Bold)로 그리고, 그 글꼴에 없는 글자(한글 등)가 들어오면 그 라벨만 시스템 글꼴로 바꾼다. 글자 폭을 재서 줄을 나눈다: 코드에서는 `.text = …` 또는 `.SetWrapped(문장, 폭)`.

## 캐릭터 디자인과 글꼴 고치기

- 캐릭터: 원본은 `Art/Characters/Tripo/TripoPartner.fbx`와 `TripoPartner_Albedo.png`(Tripo에서 받은 그대로, 뼈대 없는 T포즈). `Editor/PartnerModelBuilder.cs`가 이것을 뼈대에 스키닝하고, 눈을 따로 떼고, 구멍을 메우고, 밖에서 안 보이는 면을 표시해 `PartnerBody_momo` / `PartnerEyeL_momo` / `PartnerEyeR_momo` 메시를 만든다. 모델을 바꾸려면 같은 T포즈로 뽑은 파일로 두 파일을 교체하고 저작 메뉴를 돌린다. 체형이 많이 다르면 `Shared/Skeleton.cs`의 관절 위치도 맞춰야 한다(도구가 뼈대와 안 맞는 부위를 오류로 알려 준다).
- 확인: 메뉴 **LivingPartner ▸ Render Partner Check Sheets**가 모든 포즈(`artifacts/partner-pose-sheet.png`)와 화면에 잘린 모습(`artifacts/partner-cut-sheet.png`)을 그려 준다.
- 글꼴: `Editor/StoryFontBuilder.cs`의 `Define()`에 글자마다 획(직선 `L`, 타원 호 `A`, 점 `Dot`)이 적혀 있다. 격자는 기준선 0, 소문자 높이 5, 대문자 높이 7. 굵기는 `Build()`의 펜 반지름 두 값.
- 고친 뒤 아래 저작 메뉴를 다시 돌리고 `CharacterGallery.unity`에서 확인한다. 글꼴 파일을 내려받아 쓰지 않는다(라이선스 확인 전에는).

## 다시 만들기 / 손으로 고치기

- 메뉴 **LivingPartner ▸ Author Assets, Prefabs and Scenes** (`Editor/LPSceneBuilder.cs`, Unity MCP로도 실행 가능)가 `Art/`, `Prefabs/`, `Scenes/`를 다시 만들고, 비어 있는 직렬화 참조가 없는지 검사한다(결과: 저장소의 `artifacts/scene-build.txt`).
- 이 메뉴는 프리팹과 씬 파일을 **덮어쓴다**. 하이어라키에서 손으로 고친 내용을 유지하려면 메뉴를 다시 돌리지 말거나, 같은 값을 `LPSceneBuilder`에도 반영한다.
- 새 오브젝트가 필요하면 하이어라키/프리팹에 추가하고 스크립트에는 `[SerializeField]` 필드를 하나 추가해 연결한다. 런타임 생성 코드나 이름 검색은 쓰지 않는다.
