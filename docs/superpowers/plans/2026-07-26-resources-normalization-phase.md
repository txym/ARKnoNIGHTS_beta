# Resources 单位资源规范化（阶段一）实施计划

> 范围：本阶段只迁移 `Assets/Resources` 及其直接消费者。`Assets/GameData/Units/Json/*.json` 的文件名保持现状，留待后续阶段单独迁移；为使头像引用与实际 Resources 资源一致，仅更新其中的 `profilePictureResourceName` 值。

## 目标与验收

- 三个保留单位的模型目录规范为 `Characters/<typeId>_<resourceKey>`：
  - `1000_gopro`
  - `5503_arcslma`
  - `5504_arcslmi`
- 三个头像规范为 `ProfilePicture/UIImage_<typeId>_<resourceKey>.png`。
- 删除没有对应当前单位源数据、且用户明确不保留的 `Characters/go` 与 `Characters/mdgint`，包括目录 `.meta`。
- 运行时、编辑器目录生成器、Spine 探针、HUD 回退头像和直接相关测试全部使用同一目录规则；`resourceKey` 仍只保存短键，不添加 `resourceFolderName` 字段。
- 通过 Unity 重新生成 `Resources/BattleData/unit-catalog-v1.json`；`PlayerData` 只审计，不改其根路径或现有版本化文件名。
- 每个移动后的文件和目录 `.meta` 保留原 GUID；无旧资源路径的有效引用。

## 约束、风险与停止条件

- 不修改 `Assets/GameData/Units/Json` 的名称、单位数值或模型名；仅将 gopro/arcslma 的 `profilePictureResourceName` 改为规范头像名。本阶段目录生成后的 `sourceFile` 仍为原文件名。
- 不改动 `Resources` 的其他分类、Spine runtime、场景、Prefab 或 Package。
- 当前已有 Unity Editor 进程；不得启动第二个 Editor 或 batchmode 进程。等待该进程退出后，才执行生成器、EditMode/PlayMode 测试和真实 Spine 探针。
- 所有资源移动使用显式的 `git mv`，并将每个同名 `.meta` 一并移动；删除前先确认精确目录及其引用。
- 如果保留目录/头像的 GUID 不一致、存在计划外的旧路径引用，或 Unity 生成器/测试失败，停止在该检查点并报告证据，不通过手改生成目录掩盖问题。

## 实施步骤

### 1. 建立可验证的规范路径规则与测试预期

修改或新增仅覆盖本次变化的 EditMode/PlayMode 断言，预期：

- `typeId=1000, resourceKey=gopro` 组合为 `1000_gopro`；另外两项为 `5503_arcslma`、`5504_arcslmi`。
- 目录目录中的 Spine 路径和头像路径使用新的全名。
- `resourceKey`、底层 Spine 文件名（包括 gopro 的 `_3`）及现有源 JSON 文件名不变；头像字段改为对应的规范头像名。

测试先于运行时代码写入；因 Editor 正在运行，红灯状态在其退出后通过实际批处理结果确认。

### 2. 集中实现路径组合并更新消费者

在运行时数据层新增一个无 Unity I/O 副作用的共享路径帮助器，以 `typeId` 和短 `resourceKey` 生成目录名和 `Characters/...` / `ProfilePicture/...` Resources 路径。不要新增序列化字段。

修改：

- `UnitFactory`：加载 Spine 资源时使用共享目录规则。
- `UnitCatalogGenerator`：生成目录时使用同一规则；`sourceFile` 继续由当前源 JSON 文件名产生。
- `Task004aSpineProbe`：三个探针使用新目录。
- `BattleHudSceneCoordinator`：更新三个回退头像 Resources 路径。

同时将 gopro/arcslma 源 JSON 的 `profilePictureResourceName` 分别更新为 `UIImage_1000_gopro`、`UIImage_5503_arcslma`；arcslmi 已是 `UIImage_5504_arcslmi`，无需改动。

### 3. 迁移受控 Resources 资产

在确认追踪状态与 GUID 后执行下列成对移动：

- `Characters/gopro` + `gopro.meta` → `Characters/1000_gopro` + `1000_gopro.meta`
- `Characters/arcslma` + `arcslma.meta` → `Characters/5503_arcslma` + `5503_arcslma.meta`
- `Characters/arcslmi` + `arcslmi.meta` → `Characters/5504_arcslmi` + `5504_arcslmi.meta`
- `ProfilePicture/UIImage_gopro.png` + `.meta` → `UIImage_1000_gopro.png` + `.meta`
- `ProfilePicture/UIImage_arcslma.png` + `.meta` → `UIImage_5503_arcslma.png` + `.meta`

`UIImage_5504_arcslmi.png` 已符合规则，保持其文件和 `.meta` 不变。删除确认无现存消费者的 `Characters/go`、`Characters/mdgint` 及各自目录 `.meta`。不触碰任何其他 `Resources` 文件。

### 4. 重新生成并审计 Resources 数据

在没有其他 Unity Editor 时按现有项目命令运行 `UnitCatalogGenerator.Generate`，让其重写 `Resources/BattleData/unit-catalog-v1.json`。检查：

- 新的 skeleton/portrait resource path 均为规范路径；
- typeId、数值、动画名称、短 `resourceKey` 和源 JSON 文件名不变；
- `PlayerData` 的 `typeId` 引用仍然可解析，且其文件名与路径没有改动；
- 无 `Characters/gopro`、`Characters/arcslma`、`Characters/arcslmi` 或旧头像名称的有效代码/目录引用（历史任务文档例外）。

### 5. 验证与交付检查

按单一 Editor 队列执行：目录生成、Spine 探针、相关 EditMode 与 PlayMode 测试。记录每条命令、退出码、测试数与日志路径；没有结果 XML 的测试标记为未验证。

最后检查：Git diff、移动检测、`.meta` GUID 前后对照、删除目录的引用搜索、生成目录内容和工作树状态。若测试中暴露本次无关的既有失败，明确区分，不修改其期望来取得绿灯。

## 停止条件

完成本计划后停止；`Assets/GameData/Units/Json` 的 `<编号_英文名>.json` 重命名、未来短键子文件夹和外部下载资源的扩容不属于本阶段。
