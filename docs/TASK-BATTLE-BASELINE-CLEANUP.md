# Battle 基线漂移修复 Agent 提示词

> 状态：待在独立 worktree 中实施。
>
> 本任务只处理 LAN Match M1 全量回归暴露出的四组既有 Battle 测试失败；不得修改 `ARKnoNIGHTS.Match`，不得把 Battle 修复混入 M2。

## 1. 任务目标

在不改变已确认战斗规则的前提下，把以下问题分为“测试事实过期”“平台相关测试”“夹具时间窗不足”和“真实时间线待诊断”分别处理：

1. `5503` 精英 0 技能描述测试仍期望空字符串；
2. `UnitSourceConsumerEditModeTests` 直接对工作树原始字节计算冻结目录哈希，受 CRLF/LF 影响；
3. PlayMode 召唤隔离夹具的 `MaxTicks=101` 不再覆盖攻击动画锁结束后的延迟施法；
4. 真实对局动态果冻总数期望 `27`、实际 `24`，缺少一次三单位召唤。

前三项已有足够证据修正测试或夹具；第四项必须先定位原因，不能直接接受 `24`。

## 2. 分支、基线与边界

- 使用独立 worktree，建议分支名 `codex/battle-baseline-cleanup`。
- 基于已经包含 Match M1 的 `txym` 创建，最低必须包含 `c615a75`。
- 开始前阅读根目录 `AGENTS.md`、`docs/SPEC.md`、`docs/TEST_PLAN.md`、`docs/ARCHITECTURE.md` 以及本文件。
- 开始前执行 `git status --short`，确认没有 Unity 进程占用该 worktree。
- 不修改 `Assets/Game/Runtime/Match/`、Match 测试、Lobby、场景、Prefab、Package 或 ProjectSettings。
- 不升级 Unity 或安装新依赖。
- 不把全量测试失败简单视为 M1 失败，也不借本任务改变玩家可见战斗规则。

## 3. 已确认事实

### 3.1 5503 描述

`Assets/GameData/Units/EliteVariants/Json/5503_arcslma.json` 的 authored 描述为：

```text
每隔一段时间，分裂出三个<果冻丁>。
```

BONDS 规范和对应能力实现与该描述一致。`UnitEliteVariantSourceEditModeTests.RealSources_ResolveExpectedVariantFacts(5503, Elite0)` 的空字符串期望已过期。

处理要求：

- 把真实源测试数据改为逐用例携带预期描述，或采用等价的明确事实表达；
- 只更新 `5503 / Elite0` 的已确认期望，不把所有描述统一改成非空，也不降低断言。

### 3.2 冻结目录哈希

`UnitSourceConsumerEditModeTests` 期望：

```text
359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA
```

当前 Windows 工作树启用 `core.autocrlf=true`，检出的 `unit-catalog-v1.json` 含 `107` 个 CRLF；原始字节 SHA-256 为：

```text
BE09A6CE835369A52040B76F967AA0D853CD481D83CE909D0DCDFD3C033C1BD8
```

将换行规范化为 LF 后仍精确得到冻结常量 `359C...`，因此没有目录语义漂移。

处理要求：

- 保留 `359C...`；
- 优先在测试哈希 helper 中把 CRLF 和孤立 CR 规范化为 LF 后再按 UTF-8 计算；
- 增加至少覆盖 LF 与 CRLF 输入得到同一摘要的测试；
- 不把期望改成机器相关的 `BE09...`；
- 不为本任务批量重写仓库换行。若选择 `.gitattributes`，必须先证明不会造成大范围无关 diff。

### 3.3 隔离召唤夹具

`BattlePresentationPlaybackPlayModeTests.RealCatalog_DynamicArcslmiReplayDisposesAndRecreatesExactIds` 预期动态 ID：

```text
-1
-2
-3
```

当前实际为空。夹具使用真实 `5503` 且 `MaxTicks=101`；在单位处于攻击动画锁时，满 SP 施法会等待攻击动画结束后的合法 Tick，101 Tick 已不足以观察首次召唤。

处理要求：

- 保留“三只召唤物”和规范负 ID `-1/-2/-3` 的语义断言；
- 最小修正夹具，可延长 `MaxTicks` 或让施法者在该隔离测试中不进入普通攻击；
- 优先选择不依赖恰好某一帧动画时序、同时仍验证真实目录能力接线的方案；
- 不删除动态 ID 断言，不通过伪造回放事件绕过 Battle Core。

### 3.4 真实对局 27 → 24

失败测试：

- `BattleDemoCoordinatorEditModeTests.RealBattle_StartPauseContinueReplayAndViewKeepCompletedResultImmutable`
- `BattleDemoCoordinatorPlayModeTests.SampleScene_BattleDemoRootRunsTheRealCatalogToCompletion`

两者期望动态果冻总数 `27`，实际 `24`。`SUMMON_JELLY_MINIONS` 的 authored 源、生成目录和 `docs/SPEC.md` 没有对应规则变更；相关提交也没有修改该召唤规则。因此当前证据不足以把正式期望改为 `24`。

处理要求：

1. 先增加或使用 focused 诊断，记录每次 5503 施法/召唤的 Tick、施法者 UnitId、当时 SP、攻击锁和存活状态；
2. 找出少掉的一次召唤属于哪个施法者、应发生在哪个 Tick，以及被延迟、取消还是未获得施法机会；
3. 对照 `docs/SPEC.md` 与现有 focused Battle Core cadence 测试判断：
   - 若实现违反规则，修复 Core 并增加能在修复前失败的回归测试；
   - 若 `27` 可被证明依赖已经明确变更的正式时间线，停止并把规则冲突、证据和推荐新期望交给主 Planner，不得自行修改期望；
   - 若 Demo/集成测试对精确总数的断言层级不合适，可提出把集成测试改为语义断言的方案，但必须保留 focused Core 对精确时序和召唤次数的覆盖，并取得主 Planner 确认后再改。

## 4. 失败基线

M1 worktree 中的原始结果：

- `Artifacts/LanMatchDomain/Final-Full-EditMode-Retry1/EditModeResults.xml`
  - `424 total / 419 passed / 5 failed`
- `Artifacts/LanMatchDomain/Final-Full-PlayMode/PlayModeResults.xml`
  - `72 total / 70 passed / 2 failed`

五项 EditMode 失败中，三项哈希失败共享同一换行根因；PlayMode 两项分别对应真实对局总数和隔离召唤夹具。

## 5. 实施与验证顺序

1. 用原始基线或等价 focused 命令复现四组问题；无法复现时不得声称修复。
2. 先处理 5503 描述、换行无关哈希和隔离夹具，并分别运行对应 focused 测试。
3. 单独诊断真实对局 `27 → 24`，保留时间线证据。
4. 运行受影响的 Battle EditMode 与 PlayMode 测试程序集。
5. 串行运行完整 EditMode 和完整 PlayMode；每次使用新的输出目录。
6. 检查 Unity 编译、测试 XML、日志、测试数量和退出状态。
7. 执行 `git diff --check`，审查所有最终差异，不得包含生成目录、场景或 Match 改动。

验证必须满足：

- 测试数量大于零；
- `failed/skipped/inconclusive/not-run/not-runnable` 全为 `0` 才能称对应测试集通过；
- XML 未写完、Unity 超时、许可证失败或项目占用均记为未验证；
- 若真实对局问题尚未解决，应保持任务未完成并明确报告，不得用修改 `27 → 24` 收口。

## 6. 交付要求

交付给主 Planner：

1. 分支名、基线 SHA 和提交 SHA；
2. 四组问题各自的根因与处理方式；
3. 真实召唤时间线证据；
4. 修改文件清单；
5. 每条测试命令、退出码、数量、XML 和日志路径；
6. 未验证项与剩余风险；
7. 明确说明是否改变任何玩家可见规则；若有规则冲突，停止并等待确认。
