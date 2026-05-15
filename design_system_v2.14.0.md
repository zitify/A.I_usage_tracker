# A.I. Usage Tracker — 디자인 시스템 (v2.14.0)

이 문서는 "A.I. Usage Tracker" v2.14.0 WPF 데스크톱 애플리케이션의 디자인 시스템을 정의합니다. 사용자에게 명확하고 일관된 경험을 제공하며, 효율적인 개발 및 유지보수를 지원하는 것을 목표로 합니다.

---

## 1. 디자인 원칙

"A.I. Usage Tracker"는 AI 사용량 데이터를 직관적으로 파악하고 관리할 수 있도록 다음 디자인 원칙을 준수합니다.

1.  **정보 밀도 및 직관성**: 핵심 데이터를 한눈에 파악할 수 있도록 정보 밀도를 최적화하고, 복잡한 데이터도 명확하고 직관적인 시각화로 전달합니다. 사용자는 최소한의 클릭으로 필요한 정보를 얻을 수 있어야 합니다.
2.  **데이터 정합성 및 신뢰성**: 모든 데이터는 정확하고 최신 상태로 표시되어야 합니다. 시각적 요소는 데이터의 신뢰성을 높이고, 오류나 불일치는 명확하게 사용자에게 전달합니다.
3.  **한국어 가독성 최우선**: 모든 텍스트는 한국어 사용자에게 최적화된 폰트와 간격, 사이즈를 사용하여 높은 가독성을 보장합니다. 전문 용어는 최소화하고 친숙한 표현을 사용합니다.
4.  **다크 테마 우선 경험**: 기본적으로 다크 테마를 제공하여 눈의 피로를 줄이고 데이터를 돋보이게 합니다. 모든 컴포넌트는 다크 테마에서 완벽하게 작동하도록 설계된 후 라이트 테마로 확장됩니다.
5.  **테마 간 일관성 유지**: 두 가지 테마(Dark, Dog) 간에 핵심 레이아웃, 기능 배치, 상호작용 방식은 일관되게 유지합니다. 테마 변경은 시각적 전환 경험을 제공하되, 사용자의 학습 부담을 주지 않아야 합니다.

---

## 2. 컬러 토큰

애플리케이션의 모든 색상은 정의된 컬러 토큰을 통해 관리됩니다. 이는 테마 전환 및 일관성 유지에 필수적입니다.

| 카테고리    | 토큰           | Dark 테마 HEX | Dog 테마 HEX    | 용도                                                 |
| :---------- | :------------- | :------------ | :-------------- | :--------------------------------------------------- |
| **배경**    | `bg-base`      | `#0F0F0F`     | `#FAF4E4`       | 애플리케이션의 기본 배경색                           |
|             | `bg-card`      | `#1E1E1E`     | `#FFFDF7`       | 정보 카드, 패널 등 섹션 분리 배경색                  |
|             | `bg-input`     | `#1A1A1A`     | `#FEFCF5`       | 입력 필드, 드롭다운 등 사용자 입력 요소 배경색       |
|             | `bg-hover`     | `#262626`     | `#F2E4CC`       | 인터랙티브 요소(버튼, 리스트)에 마우스 오버 시 배경색 |
|             | `bg-active`    | `#333333`     | `#E8D4B0`       | 선택된 요소, 클릭 시 활성화 상태 배경색              |
|             | `bg-titlebar`  | `#141414`     | `#5C2E0E`       | 애플리케이션 상단 타이틀 바 배경색                   |
| **경계선**  | `border-default` | `#2A2A2A`     | `#D4B896`       | 일반적인 컴포넌트 경계선, 구분선                     |
|             | `border-strong`| `#3A3A3A`     | `#C09878`       | 강조된 경계선, 주요 구분선                           |
| **텍스트**  | `text-default` | `#E8E8E8`     | `#3D2B1F`       | 대부분의 본문 텍스트, 라벨                           |
|             | `text-high`    | `#F5F5F5`     | `#2C1F14`       | 제목, 강조 텍스트, 주요 정보 텍스트                  |
|             | `text-sub`     | `#888888`     | `#7A5A40`       | 보조 정보, 메타 데이터, 약한 강조 텍스트             |
|             | `text-hint`    | `#9A9A9A`     | `#7A5A40`       | 입력 필드 플레이스홀더, 비활성화 텍스트 (Dog 테마는 sub와 동일) |
| **강조색**  | `accent-green` | `#4ADE80`     | `#2060A0` (Blue) | 긍정적 지표, 주요 액션 버튼 등 (Dog 테마는 Primary Blue) |
|             | `accent-blue`  | `#60A5FA`     | `#2060A0`       | 정보성 강조, 보조 버튼 등                            |
|             | `accent-yellow`| `#FACC15`     | `#B85220` (Orange) | 경고, 주의 필요 지표 등 (Dog 테마는 Warn Orange)   |
|             | `accent-red`   | `#F87171`     | `#C04030`       | 부정적 지표, 위험성 경고, 삭제 버튼 등               |
| **브랜드**  | `brand-openai` | `#10A37F`     | `#10A37F`       | OpenAI 관련 요소에 사용되는 브랜드 색상 (테마 공통)  |
| **상태**    | `status-good`  | `#4ADE80`     | `#4ADE80`       | 성공, 정상 상태                                      |
|             | `status-warn`  | `#FACC15`     | `#B85220`       | 경고, 주의 상태                                      |
|             | `status-high`  | `#FB923C`     | `#FB923C`       | 높은 경고, 임박한 위험 상태                          |
|             | `status-bad`   | `#F87171`     | `#C04030`       | 오류, 실패, 위험 상태                                |

*Dog 테마 배경 패턴: 강아지 발자국 SVG 타일 (opacity 0.05-0.1, `bg-base` 위에 오버레이)*

---

## 3. 타이포그래피 스케일

애플리케이션 내 모든 텍스트는 일관된 타이포그래피 스케일을 따릅니다. 기본 폰트는 Segoe UI를 사용하며, 코드 및 모노스페이스 텍스트에는 Consolas를 사용합니다.

| 분류          | 폰트 패밀리             | 사이즈 (px) | Weight      | Line-height (px) | 용도                                               |
| :------------ | :---------------------- | :---------- | :---------- | :--------------- | :------------------------------------------------- |
| **페이지 타이틀** | Segoe UI (기본) / Noto Sans KR (Fallback) | 28          | SemiBold    | 36               | 각 페이지의 주요 제목 (`Dashboard`, `Settings`)    |
| **섹션 헤더** | Segoe UI (기본) / Noto Sans KR (Fallback) | 20          | SemiBold    | 28               | 대시보드 내 섹션 제목 (`Usage Summary`, `Cost Trends`) |
| **KPI 숫자**  | Segoe UI (기본) / Noto Sans KR (Fallback) | 36          | Bold        | 44               | KPI 카드 내 핵심 숫자 (`Total Tokens`, `Avg Cost`) |
| **본문**      | Segoe UI (기본) / Noto Sans KR (Fallback) | 14          | Regular     | 22               | 일반적인 정보 텍스트, 리스트 아이템, 버튼 라벨   |
| **메타**      | Segoe UI (기본) / Noto Sans KR (Fallback) | 12          | Regular     | 18               | 보조 정보, 단위, 타임스탬프, 힌트 텍스트           |
| **모노스페이스** | Consolas (기본) / D2Coding (Fallback) | 13          | Regular     | 20               | 코드 조각, ID, 로그 메시지 등                      |

---

## 4. 간격 시스템

4px 베이스 그리드 시스템을 사용하여 예측 가능하고 일관된 레이아웃을 구현합니다. 모든 간격은 4px의 배수여야 합니다.

*   **기본 단위**: `4px`
*   **컴포넌트 내부 패딩**:
    *   **KPI 카드**: `padding: 16px`
    *   **DataGrid 행**: `padding: 8px 16px`
    *   **버튼**: `padding: 8px 16px` (세로 8px, 가로 16px)
    *   **입력 필드**: `padding: 8px 12px`
*   **컴포넌트 간 간격**:
    *   **작은 간격 (inline elements)**: `8px`
    *   **중간 간격 (stacked elements, items within a list)**: `12px`
    *   **큰 간격 (sections, cards)**: `16px`
    *   **매우 큰 간격 (major layout divisions)**: `24px`, `32px`
*   **컨테이너 패딩**:
    *   **메인 콘텐츠 영역**: `padding: 24px`
    *   **모달/팝업**: `padding: 20px`

```xaml
<!-- 예시: StackPanel 내 간격 -->
<StackPanel Orientation="Vertical" Spacing="12">
    <TextBlock Text="섹션 헤더" FontSize="20" FontWeight="SemiBold"/>
    <Button Content="액션 버튼"/>
</StackPanel>

<!-- 예시: Grid 패딩 -->
<Border Background="{DynamicResource bg-card}" Padding="16">
    <!-- 카드 내용 -->
</Border>
```

---

## 5. 컴포넌트 스펙

### 5.1. 탭 헤더 (좌측 네비게이션)

좌측 내비게이션 영역에서 각 페이지로 이동하는 데 사용됩니다.

*   **Anatomy**: 아이콘 (16x16px) + 텍스트 라벨
*   **사이즈**:
    *   높이: `44px`
    *   폭: 내비게이션 패널 폭 (`200px` 기본)
    *   아이콘 크기: `16x16px`
    *   텍스트: `text-default`, `본문 (14px, Regular)`
*   **상태**:
    *   **Default**: `bg-base`, `text-default`
    *   **Hover**: `bg-hover`, `text-high`
    *   **Active (선택됨)**: `bg-active`, `text-high`, 좌측에 `accent-blue` (2px 두께) 활성화 인디케이터

### 5.2. KPI 카드

핵심 성과 지표를 요약하여 보여주는 카드 형태의 컴포넌트입니다.

*   **Anatomy**:
    1.  라벨 (메타, 상단 좌측)
    2.  큰 숫자 (KPI 숫자, 중앙)
    3.  보조 메타 (메타, 하단 우측)
*   **사이즈**: 가변 (Grid Column에 맞게 조정), 최소 `Width: 160px`, `Height: 120px`
*   **패딩**: `16px` (모든 방향)
*   **배경**: `bg-card`
*   **경계선**: `border-default`
*   **텍스트**:
    *   **라벨**: `text-sub`, `메타 (12px, Regular)`
    *   **큰 숫자**: `text-high`, `KPI 숫자 (36px, Bold)`
    *   **보조 메타**: `text-sub`, `메타 (12px, Regular)`

### 5.3. 액션 버튼

사용자 인터랙션을 유도하는 주요 버튼입니다. Primary, Secondary, Danger 세 가지 유형이 있습니다.

*   **Anatomy**: 텍스트 라벨 (선택적으로 아이콘 포함)
*   **사이즈**:
    *   높이: `36px`
    *   패딩: `8px 16px` (세로 8px, 가로 16px)
    *   경계선 반경: `4px`
    *   텍스트: `본문 (14px, Regular)`
*   **상태**:

    | 유형      | 상태        | 배경색         | 텍스트색       | 경계선         |
    | :-------- | :---------- | :------------- | :------------- | :------------- |
    | **Primary** | Default     | `accent-green` | `text-high`    | `Transparent`  |
    |           | Hover       | `#3EB070`      | `text-high`    | `Transparent`  |
    |           | Active      | `#32885A`      | `text-high`    | `Transparent`  |
    |           | Disabled    | `#2A3F33`      | `text-sub`     | `Transparent`  |
    | **Secondary** | Default   | `Transparent`  | `text-default` | `border-default` |
    |           | Hover       | `bg-hover`     | `text-high`    | `border-strong`  |
    |           | Active      | `bg-active`    | `text-high`    | `border-strong`  |
    |           | Disabled    | `Transparent`  | `text-sub`     | `border-default` |
    | **Danger** | Default     | `accent-red`   | `text-high`    | `Transparent`  |
    |           | Hover       | `#C45A5A`      | `text-high`    | `Transparent`  |
    |           | Active      | `#9B4848`      | `text-high`    | `Transparent`  |
    |           | Disabled    | `#3A2424`      | `text-sub`     | `Transparent`  |

### 5.4. 인풋 / 콤보박스

사용자의 텍스트 입력 또는 옵션 선택을 위한 컴포넌트입니다.

*   **Anatomy**: 라벨 (옵션) + 입력 필드 / 선택 드롭다운
*   **사이즈**:
    *   높이: `36px`
    *   패딩: `8px 12px`
    *   경계선 반경: `4px`
    *   텍스트: `본문 (14px, Regular)`
*   **상태**:
    *   **Default**: `bg-input`, `border-default`, `text-default`, 플레이스홀더 `text-hint`
    *   **Hover**: `bg-input`, `border-strong`
    *   **Focus**: `bg-input`, `border-strong`, `accent-blue` (1px 두께) 아웃라인
    *   **Disabled**: `bg-card`, `border-default`, `text-hint`

### 5.5. DataGrid 행

테이블 형태로 데이터를 표시할 때 사용되는 각 행입니다.

*   **Anatomy**: 각 셀의 텍스트 데이터
*   **사이즈**:
    *   높이: `36px`
    *   패딩: `8px 16px` (세로 8px, 가로 16px)
    *   텍스트: `본문 (14px, Regular)`
*   **상태**:
    *   **Default**: `Transparent` 배경, `text-default`
    *   **Hover**: `bg-hover` 배경, `text-high`
    *   **Selected**: `bg-active` 배경, `text-high` (또는 강조선)

### 5.6. 상태 배지

항목의 현재 상태를 간략하게 표시하는 작은 라벨입니다.

*   **Anatomy**: 텍스트 라벨 (선택적으로 작은 아이콘)
*   **사이즈**:
    *   높이: `20px`
    *   패딩: `2px 8px`
    *   경계선 반경: `10px` (pill 형태)
    *   텍스트: `메타 (12px, Regular)`
*   **상태**:
    *   **Good**: 배경 `status-good` (opacity 0.2), 텍스트 `status-good`
    *   **Warn**: 배경 `status-warn` (opacity 0.2), 텍스트 `status-warn`
    *   **High**: 배경 `status-high` (opacity 0.2), 텍스트 `status-high`
    *   **Bad**: 배경 `status-bad` (opacity 0.2), 텍스트 `status-bad`

### 5.7. 빈 상태(Empty state) 블록

데이터가 없을 때 사용자에게 다음 단계를 안내하는 블록입니다.

*   **Anatomy**:
    1.  제목 (`섹션 헤더` 스타일)
    2.  설명 (`본문` 스타일)
    3.  안내 아이콘/일러스트 (중앙)
    4.  Primary 액션 버튼 (선택적)
*   **사이즈**: 콘텐츠 영역에 맞게 가변
*   **배경**: `bg-card` 또는 `Transparent`
*   **레이아웃**: 중앙 정렬, 상하 간격 `24px`
*   **패턴**: 4단계 안내 카드
    1.  **Title**: "데이터를 찾을 수 없습니다." 또는 "아직 기록된 AI 사용 내역이 없습니다."
    2.  **Illustration/Icon**: 해당 섹션과 관련된 심볼릭 아이콘
    3.  **Description**: "AI 서비스 연동 후 첫 사용 내역이 여기에 표시됩니다." 또는 "새로운 AI 세션을 시작하여 사용 내역을 기록해 보세요."
    4.  **Action**: "AI 서비스 연동하기" 또는 "새 세션 시작하기" (Primary 버튼)

### 5.8. 토스트 / 인라인 알림

사용자에게 중요하지 않지만 즉각적인 피드백을 제공하는 메시지입니다.

*   **Anatomy**: 아이콘 (선택적) + 메시지 텍스트 + 닫기 버튼 (옵션)
*   **사이즈**:
    *   높이: `48px` (기본)
    *   패딩: `12px 16px`
    *   경계선 반경: `4px`
    *   텍스트: `본문 (14px, Regular)`
*   **위치**:
    *   **토스트**: 화면 상단/하단 중앙 또는 우측에 잠시 표시 후 사라짐.
    *   **인라인**: 관련 컴포넌트 바로 위/아래에 고정적으로 표시.
*   **상태**: (일반적으로 `status-good`, `status-warn`, `status-bad`에 매핑)
    *   **성공 (Good)**: 배경 `status-good` (opacity 0.1), 경계선 `status-good`, 텍스트 `status-good`
    *   **정보 (Info)**: 배경 `accent-blue` (opacity 0.1), 경계선 `accent-blue`, 텍스트 `accent-blue`
    *   **경고 (Warn)**: 배경 `status-warn` (opacity 0.1), 경계선 `status-warn`, 텍스트 `status-warn`
    *   **오류 (Bad)**: 배경 `status-bad` (opacity 0.1), 경계선 `status-bad`, 텍스트 `status-bad`

---

## 6. 아이콘 가이드

주요 AI 브랜드 아이콘 및 시스템 아이콘 사용에 대한 가이드입니다.

*   **AI 브랜드 마크**:
    *   **Claude**: 공식 로고 심볼 (풀컬러 또는 `text-default` / `text-high` 색상)
    *   **OpenAI**: 원형 ◯ 심볼 (공식 `brand-openai` 색상 또는 `text-default` / `text-high` 색상)
    *   **Gemini**: 공식 로고 심볼 (풀컬러 또는 `text-default` / `text-high` 색상)
    *   **Grok**: 공식 로고 심볼 (풀컬러 또는 `text-default` / `text-high` 색상)
    *   **Copilot**: 공식 로고 심볼 (풀컬러 또는 `text-default` / `text-high` 색상)
*   **크기**:
    *   **내비게이션 아이콘**: `16x16px`
    *   **인라인 아이콘 (버튼, 리스트)**: `16x16px`
    *   **헤더 아이콘 (섹션 제목 옆)**: `20x20px`
    *   **대시보드 KPI 카드 내 아이콘**: `24x24px`
    *   **빈 상태 블록 일러스트**: `48x48px` 또는 `64x64px` (심볼릭)
*   **색상**:
    *   **시스템 아이콘**: `text-default` 또는 `text-sub` 사용.
    *   **액티브/호버 상태**: `text-high` 또는 `accent-blue` 사용.
    *   **AI 브랜드 아이콘**: 가능한 경우 공식 브랜드 컬러를 사용하지만, 일관성 유지를 위해 `text-default` 색상으로 단색 처리할 수도 있습니다. 배경색이 어두울 경우 대비를 위해 밝은 색상(`text-high`)을 사용합니다.

---

## 7. 상태 표현

애플리케이션 내에서 다양한 상태(정상, 경고, 위험 등)를 시각적으로 명확하게 전달합니다.

*   **토큰 매핑**:
    *   `status-good`: 성공, 완료, 정상 범위 내
    *   `status-warn`: 주의, 임박한 문제, 설정 필요
    *   `status-high`: 높은 경고, 임박한 위험, 중요 조치 필요
    *   `status-bad`: 실패, 오류, 위험, 한도 초과
*   **진행률 바 (Progress Bar)**:
    *   **일반 진행**: `accent-blue`
    *   **성공/완료**: `status-good`
    *   **경고 (일부 실패)**: `status-warn`
    *   **오류/실패**: `status-bad`
*   **사용량 % 색 임계값**:
    *   `< 50%`: `status-good` (초록색) - 정상, 여유 있음
    *   `50% – 75%`: `status-warn` (노란색) - 중간 사용, 주의
    *   `75% – 90%`: `status-high` (주황색) - 높은 사용량, 한도 임박
    *   `> 90%`: `status-bad` (빨간색) - 한도 초과 또는 매우 임박, 조치 필요

---

## 8. 데이터 시각화

차트 및 데이터그리드를 통해 AI 사용량 데이터를 시각적으로 표현합니다.

*   **차트 색상 팔레트 (시리즈 6색)**: 데이터 시리즈가 많을 경우 순환하여 사용합니다.
    1.  `accent-blue` (`#60A5FA` / `#2060A0`) - 기본 강조
    2.  `accent-green` (`#4ADE80` / `#4ADE80`) - 긍정적 지표
    3.  `accent-yellow` (`#FACC15` / `#B85220`) - 주의 지표
    4.  `accent-red` (`#F87171` / `#C04030`) - 부정적 지표
    5.  `#8B5CF6` (Purple, Dark/Dog 공통) - 보조
    6.  `#EC4899` (Pink, Dark/Dog 공통) - 보조

    *Dark 테마 기준 색상 팔레트: `60A5FA`, `4ADE80`, `FACC15`, `F87171`, `8B5CF6`, `EC4899`*
    *(Dog 테마는 대비를 위해 일부 변경될 수 있으나, 색상 계열은 유지)*

*   **그리드 및 축 색상**:
    *   **그리드 라인**: `border-default` (얇은 선)
    *   **축 라벨**: `text-sub`
    *   **축 라인**: `border-strong` (선택적)
*   **한국어 라벨 처리**:
    *   **폰트**: `본문 (14px, Regular)` 또는 `메타 (12px, Regular)` 사용, `Noto Sans KR` fallback
    *   **간격**: 차트 공간을 고려하여 텍스트 오버랩이 발생하지 않도록 충분한 간격을 확보
    *   **줄바꿈**: 필요한 경우, 짧고 의미 있는 단위로 자동 또는 수동 줄바꿈을 허용하여 가독성 유지

---

## 9. 접근성 & 한국어 가독성

모든 사용자가 애플리케이션을 쉽게 이용하고, 한국어 텍스트가 명확하게 읽힐 수 있도록 접근성을 고려합니다.

*   **명도 대비 가이드 (WCAG 2.1 AA 기준 준수)**:
    *   **일반 텍스트**: 최소 `4.5:1` (작은 폰트)
    *   **큰 텍스트 (18pt / 24px 이상 또는 14pt / 18.66px 이상 & Bold)**: 최소 `3:1`
    *   **비텍스트 요소 (아이콘, UI 컴포넌트)**: 최소 `3:1`
    *   컬러 토큰 정의 시 이 기준을 준수하도록 설계되었습니다.
*   **한국어 폰트 Fallback**:
    *   기본 폰트(`Segoe UI`)에 한국어 문자가 포함되지 않거나 지원이 불안정할 경우, `Noto Sans KR`을 우선적으로 사용하고, 시스템 기본 폰트(`맑은 고딕`)를 최종 fallback으로 지정합니다.
    *   모노스페이스 폰트의 경우 `D2Coding`을 `Consolas`의 한국어 fallback으로 사용합니다.
*   **최소 폰트 사이즈**:
    *   애플리케이션 내 모든 텍스트는 `12px`(`메타` 스타일) 이상을 유지합니다. `12px` 미만의 폰트 사용은 특별한 경우(예: 저작권 표시 등)를 제외하고 지양합니다.

---

## 10. 두 테마 사용 가이드

"A.I. Usage Tracker"는 다크 테마와 "Dog" 테마 두 가지를 제공하여 사용자의 취향과 환경에 맞는 시각적 경험을 제공합니다.

*   **언제 Dog 테마가 적합한가**:
    *   **개인 취향**: 라이트 테마를 선호하는 사용자.
    *   **밝은 작업 환경**: 햇빛이 강하거나 주변 조명이 밝은 환경에서 시인성을 높이고 싶은 사용자.
    *   **따뜻하고 친근한 분위기 선호**: 차분하고 부드러운 색감으로 더 편안한 느낌을 선호하는 사용자.
    *   **시각적 변화 추구**: 다크 테마에 익숙해진 후 새로운 시각적 경험을 원하는 사용자.
*   **테마 토글 위치**:
    *   **설정(Settings) 페이지**: 테마 선택 옵션을 명확하게 제공합니다. (`좌측 탭 네비게이션` -> `설정` -> `테마` 섹션)
    *   **타이틀바 (옵션)**: 사용자 접근성을 높이기 위해 타이틀바 우측에 작은 테마 전환 아이콘(`☀️` / `🌙`)을 추가하는 것을 고려할 수 있습니다. (향후 업데이트에서 검토)
*   **발자국 패턴 사용 강도**:
    *   `Dog` 테마의 강아지 발자국 SVG 패턴은 `bg-base` 위에 약한 투명도(`opacity: 0.05-0.1`)로 오버레이되어 배경에 미묘한 질감을 더합니다.
    *   이 패턴은 주로 메인 배경(`bg-base`)에만 적용하며, 카드(`bg-card`)나 입력 필드(`bg-input`)와 같이 정보가 집중되는 영역에서는 사용하지 않아 시각적 혼란을 방지합니다.
    *   패턴의 목적은 테마의 정체성을 강화하는 것이므로, 너무 강하여 정보 가독성을 해치지 않도록 세심하게 조정합니다.
