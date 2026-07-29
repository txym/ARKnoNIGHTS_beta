# External Data Link Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在仓库 `AGENTS.md` 中新增一套强制的外部数据与文件链接安全规则，防止测试或临时目录清理通过 reparse point 损坏项目外数据。

**Architecture:** 只在工具与权限章节后新增独立的 `5.8 外部数据与文件链接安全` 小节，不改写既有审批规则。使用文本断言验证六项批准要求全部存在，并用 Git diff 确认其他规范未发生变化。

**Tech Stack:** Markdown、PowerShell、Git

## Global Constraints

- 项目目录外的数据和用户提供的事实源默认只读；读取授权不等于写入授权。
- 禁止从 `Temp/Library/Logs`、缓存、构建输出或测试临时目录链接到项目外路径、用户数据或原始事实源。
- 测试 fixture 只能复制最少必要文件，不能链接或修改原始数据。
- 递归删除、移动或清理前必须解析 reparse point 的实际目标；目标越界时停止。
- 测试后必须只读检查外部源的文件数量、必要文件集合和关键完整性指标。
- 外部数据受影响时必须立即停止、披露影响和恢复边界，不得静默修复。
- 本任务不读取、写入、移动、恢复或重新生成 `G:/ARKnoNIGHTS_tools` 中的任何文件。
- 只修改 `AGENTS.md`，不新增守卫脚本、依赖、Unity 文件或测试。

---

### Task 1: 新增并验证外部数据安全规则

**Files:**
- Modify: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-07-28-external-data-link-safety-design.md`

**Interfaces:**
- Consumes: 已确认设计中的六项强制规则。
- Produces: `AGENTS.md` 中唯一的 `### 5.8 外部数据与文件链接安全` 小节。

- [ ] **Step 1: 运行缺失规则的基线检查**

  ```powershell
  $content = Get-Content -LiteralPath 'AGENTS.md' -Raw -Encoding UTF8
  if ($content -notmatch '(?m)^### 5\.8 外部数据与文件链接安全$') {
      throw 'Missing approved external-data link safety section.'
  }
  ```

  Expected: 非零退出，错误为 `Missing approved external-data link safety section.`。

- [ ] **Step 2: 在第 5.7 节后插入批准规则**

  在 `### 5.7 外部资料与敏感信息` 的第 4 条之后、`## 6. 测试与验证` 之前插入：

  ```markdown
  ### 5.8 外部数据与文件链接安全

  1. 项目目录外的数据和用户提供的事实源默认只读。即使任务允许读取该路径，也不代表允许创建、修改、覆盖、移动或删除其中的内容；任何项目外持久写入仍需事先获得明确批准。
  2. 禁止在 `Temp/`、`Library/`、`Logs/`、缓存、构建输出、测试临时目录或其他可能被自动或递归清理的位置，创建指向项目外路径、用户数据或原始事实源的 junction、symbolic link、hard link 或其他 reparse point。
  3. 测试需要外部数据时，只能把最少必要文件复制到独立 fixture；fixture 不得链接原始目录，不得在原始文件上注入错误数据。
  4. 对目录执行递归删除、移动或清理前，必须枚举其中的 reparse point，并解析每个链接的实际目标。只要存在目标位于预期清理根目录之外的链接，就必须停止操作并报告。
  5. 使用外部数据完成测试后，必须以只读方式核对源目录的文件数量、必要文件集合和关键文件哈希或等价完整性指标。发现变化时立即停止后续任务并报告，不得继续运行更多测试。
  6. 一旦外部数据受到影响，必须说明影响范围、因果链、已停止的操作、可恢复来源和需要用户批准的恢复步骤；不得静默修复或把受损数据当作有效输入。
  ```

- [ ] **Step 3: 运行六项规则断言**

  ```powershell
  $content = Get-Content -LiteralPath 'AGENTS.md' -Raw -Encoding UTF8
  $required = @(
      '### 5.8 外部数据与文件链接安全',
      '项目目录外的数据和用户提供的事实源默认只读',
      'junction、symbolic link、hard link 或其他 reparse point',
      '只能把最少必要文件复制到独立 fixture',
      '必须枚举其中的 reparse point，并解析每个链接的实际目标',
      '文件数量、必要文件集合和关键文件哈希或等价完整性指标',
      '不得静默修复或把受损数据当作有效输入'
  )
  foreach ($text in $required) {
      if (-not $content.Contains($text)) {
          throw "Missing approved rule text: $text"
      }
  }
  if ([regex]::Matches($content, '(?m)^### 5\.8 外部数据与文件链接安全$').Count -ne 1) {
      throw 'External-data link safety section must appear exactly once.'
  }
  ```

  Expected: exit 0，无输出。

- [ ] **Step 4: 审查格式与修改范围**

  ```powershell
  git diff --check -- AGENTS.md
  git diff --unified=3 -- AGENTS.md
  git status --short -- AGENTS.md
  ```

  Expected:

  - `git diff --check` 无输出且 exit 0；
  - diff 只在第 5.7 节和第 6 章之间新增一个 5.8 小节；
  - `AGENTS.md` 是唯一被本任务修改的文件。

- [ ] **Step 5: 提交规则**

  ```powershell
  git add -- AGENTS.md
  git commit -m "docs: guard external data from linked temp cleanup"
  ```
