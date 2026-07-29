# BONDS 单位全变体资源导入计划

> 状态：已获用户确认，在主工作区执行。

## 目标

将 `docs/bonds/BONDS_SPEC.md` 的“使用单位 ID 总览”所列 93 个 TypeId 的全部现有 Spine 变体及头像导入 Unity，并保持现有 `1000_gopro` 的 Resources 结构。补齐暂存区缺失的 `1243` 与 `10039`，同时核对并规范现有 `5503_arcslma` 与 `5504_arcslmi`。

## 范围与数据契约

- 权威清单是当前主工作区未提交的 `docs/bonds/BONDS_SPEC.md`：88 个商店单位和 5 个非商店单位。
- 本次只迁移美术资源：每个变体的 `.atlas.txt`、Spine 贴图 `.png`、`.skel.bytes` 及 `UIImage_*.png` 头像；不复制 staging 中的 `manifest.json`、`unit-levels.json` 或未完成的 `unit-source-v1.json`。
- 角色资源放在 `Assets/Resources/Characters/<完整 staging 文件夹名>/`，头像放在 `Assets/Resources/ProfilePicture/UIImage_<完整 staging 文件夹名>.png`。
- Unity/Spine 自动生成的 Atlas、Material、SkeletonDataAsset 与所有 `.meta` 由 Unity 在同一工作区创建和维护，绝不手工伪造 GUID。
- `1322_wdgyht_2` 是默认形态，`1322_wdgyht` 是精二形态；两者均按原始完整名称保留，验证报告必须显式记录该语义映射，不能按后缀推断。
- `5503_arcslma` 与 `5504_arcslmi` 名称已符合规范。它们现有 atlas 与 staging 的唯一差别是 CRLF/LF；导入时统一采用 staging 的 LF 文本，其余二进制文件须保持 SHA-256 一致。

## 实施步骤

1. 解析 BONDS 清单，枚举 staging 中每个 TypeId 的所有完整变体目录，生成确定性导入清单和缺口报告。
2. 审查并使用既有的下载流程补齐 `1243`（残党萨克斯手）及 `10039`（“开怀畅饮”）的全部变体，验证网页来源、文件扩展名和 SHA-256 manifest；下载结果仅写入用户指定的 staging 目录。
3. 添加可重复运行的项目内导入脚本：只接受清单中的变体、只复制四种资源文件、拒绝未知/缺失/重复输入，并在覆盖前比对 hash。脚本不得复制 staging metadata 或任何 JSON。
4. 在主工作区执行脚本：保留既有规范资源及其 GUID；将需要规范换行的 5503/5504 atlas 以 staging 内容更新。所有新增资源都由 Unity 后续创建 `.meta`，不从 staging 拷贝 `.meta`。
5. 串行启动一次 Unity batchmode，等待脚本编译和 Spine 资产导入完成。不得与其他 Unity Editor/batchmode 实例并行。
6. 添加或执行仅检查本次资源的验证入口：每个清单项应有三件套、头像和可加载的 `SkeletonDataAsset`；通过 `Resources.Load` 验证角色与头像路径；逐项核验原始文件哈希；输出 `1322` 的默认/精二映射。
7. 复查 Git diff 和 Unity 日志，确认仅有 BONDS 所需资源、导入脚本、验证记录及必要 `.meta`；运行相关 EditMode 测试，并报告未能验证的项目。

## 风险与停止条件

- 若下载流程不存在、下载的资源 ID/英文名与 PRTS 页面不一致、或任一 Spine 三件套缺失，则停止该条目导入，不以替代资源兜底。
- 若 Unity/Spine 不能为任一变体生成可加载的 SkeletonDataAsset，保留诊断并停止，不提交半成品引用。
- 不修改 `Assets/GameData/Units/Json`、单位目录或运行时 Catalog；BONDS 仅提供 ID/羁绊信息，不能补足完整运行时单位数据。
- 当前主工作区已有用户修改：`docs/bonds/BONDS_SPEC.md`、数值分析文档、`.superpowers/` 和三张 HUD 参考图。本次不覆盖、不格式化、不暂存这些文件。

## 验证标准

- 93 个 TypeId 的每个实际变体均已列入导入报告；`1243` 与 `10039` 不再缺失。
- 每个变体的 raw Spine 三件套和头像均存在，且同 staging 的 SHA-256 一致（5503/5504 atlas 以内容逐行一致及 LF 规范化为准）。
- 每个导入变体的 `SkeletonDataAsset` 可经 Unity/Spine API 加载；头像可用 `Resources.Load<Texture2D>` 加载。
- `1322_wdgyht_2=默认`、`1322_wdgyht=精二` 通过验证输出明确确认。
- Unity 编译没有本次资源引入的新错误，相关 EditMode 测试通过或明确标记为未验证。
