---
feature: full-audit-optimization
status: delivered
updated: 2026-09-22
branch: optimize/full-audit
commits: 963a4f9..<head> # filled at delivery
---

# 全维度审查与优化落地

## Report

**What was built** — 对 v5.7.0 做五路全维度审查，汇总 90 个可优化点（见 [S1]）。按优先级落地 P0/P1 及大部分 P2：凭据与发布卫生、任务写路径统一、同步 keyset/游标/LWW/预检/分片、成长备份、打字口径、今日进度共式、循环派生幂等、UI hook/主题/更新检查/详情保存、头像按用户隔离、NetworkMonitor 探测、TaskService→IDatabaseService、README/start.bat 卫生。独立复审 2 个 critical（keyset 漏更晚行、预检永久挡首传）已修复。

**Verification** — `dotnet build` Debug 0 error；`dotnet test` **212/212** 通过（基线 181）。

**Journey log**
1. 本机 NuGet 因缺失 `ProgramFiles(x86)` 等环境变量在 `XPlatMachineWideSetting` 抛 `path1 null`；补环境变量 + `MSBUILDDISABLENODEREUSE=1` 后 restore 可用。
2. 四路并行实现代理被进程重启打断；先编译/测试收口半成品再补齐，避免直接覆盖。
3. PostgREST `Or(string)` 不可用；keyset 用「`(updated_at>A)` ∪ `(updated_at=A AND id>B)`」两路合并，不要只拉边界。
4. 预检须区分「In 成功但远端无行」（可首传）与「分片失败」（本轮跳过）；Insert 已预绑 SyncId。
5. NLP `十一` 不可被 `[一..十]` 咬成 `一`；纯小时数字分支强制「后」。
6. UseWindowsForms 不能删：`MainWindow` 用了 `System.Windows.Forms.Screen`。

**遗留** — T12 完整数据层回归网仍偏薄；T14 其余 P2/P3（Dispatcher.BeginInvoke 全量、虚拟化、无障碍、依赖升级、安装器 GUID 等）；T15 完整 DI 去 Singleton / 双 Window 合并。

## [S1] Problem

对 v5.7.0（基线 `963a4f9`）做全维度审查：缺陷/风险、架构与可维护性、性能/资源、测试与工程化、体验。五路独立深审（数据层 / 同步鉴权 / UI 生命周期 / 业务 ViewModel / 架构工程）交叉汇总后，共 **90** 个可优化点。上一轮 `CODE_REVIEW_v5.7.0.md` 中的 C1–C5、H8/H9 等多数已修复，但 H2 keyset、M7 存密码、M8 发布密钥、C5 复发残留、H5 进度口径等仍未关闭，且本轮新发现导入重复、备份丢成长数据、打字 Peek 变异、头像串号等问题。

### 问题清单（按优先级）

#### P0 严重（阻断/安全/数据损坏）

| ID | 标题 | 位置 | 要点 |
|---|---|---|---|
| A1/S6 | 发布产物可携带真实 Anon Key 且未进 .gitignore | `TodoSidebar.iss:39-42`, `.gitignore` | 凭据可被提交/安装到任意本机用户可读路径 |
| D1 | 无 SyncId 的导入每次重复插入 | `DatabaseService.cs:626-628` | 重复导入放大任务与完成权重 |
| D2 | `UpsertTaskFromRemote` 按 SyncId 整行更新 | `DatabaseService.cs:1448-1470` | 存活行+软删行可共享 SyncId，写穿回收站 |
| D3 | 备份/恢复不是全量快照 | `ExportService.cs:28-35,336` | 恢复会 `CleanupOrphanRecords` 清掉 XP/番茄/完成，且不导出成长表 |
| S1 | 分页仍是客户端 Range，非真 keyset | `SyncService.cs:626-691` | 边界 `updated_at` 挤满一页时静默漏同步 |

#### P1 高（正确性/泄漏/口径）

| ID | 标题 | 位置 | 要点 |
|---|---|---|---|
| D4 | CSV/MD 导出丢 Description/Recurrence/耗时 | `ExportService.cs:203-218` | 用户以为已备份，规则丢失 |
| D5 | `InsertTaskCore` 不写 `CompletedAt` | `DatabaseService.cs:430-431` | 完成态插入丢时间戳 |
| D6 | Tasks 写路径列清单分叉（结构性） | 多处 INSERT/UPDATE | 加列易静默丢字段（C3 已中招） |
| D7 | 本地库几乎无查询索引 | `DatabaseService.cs:194` 仅 1 个 | 热路径全表扫描 |
| D8 | 迁移脚本不 DROP LWW 破坏触发器 | `supabase_v560/v571*.sql` | 只跑迁移的环境 LWW 仍被 `now()` 击穿 |
| S2 | 下载失败行可被游标跳过 | `SyncService.cs:336-342,823-841` | 最长 24h 漏同步 |
| S3 | LWW 比较未 UTC 归一 | `SyncService.cs:748-749` | Kind 混用可判错胜者 |
| S4 | 头像缓存文件未按用户隔离 | `AccountService.cs:29-31,233-246` | 切号可显示上一账号头像 |
| S5/U16 | 「记住我」仍保存密码密文 | `LoginWindow.xaml.cs:107-154` | 违反最小凭据；session 已足够 |
| S7 | `TaskContentEquals` 缺耗时/DeletedAt | `SyncService.cs:885-901` | 远端这些字段变更被跳过 |
| U1 | WidgetWindow HwndSource hook 不摘 | `WidgetWindow.xaml.cs:366-380` | 形态切换累积 hook |
| U2 | ThemeChanged 全仓库无订阅者 | `WidgetWindow.xaml.cs:291` 等 | 亚克力/主题切换不刷新 |
| U3 | `CheckUpdate_Click` async void 无保护 | `SettingsWindow.cs:326-354` | 网络异常可崩 |
| B1 | `TypingCounterCore.Peek()` 会 Flush | `TypingCounterCore.cs:88-103` | 轮询期间字数系统性虚高 |
| B2 | `GetKeyboardLayout` 误传进程 ID | `TypingStatsService.cs:317-321` | 中英分类错误 → 字数口径错 |
| B3 | 侧边栏/统计页「今日进度」分母不一致 | `MainViewModel` vs `StatisticsViewModel` | 同一天两套完成率 |
| B4 | 循环任务幽灵实例残留 | `TaskService.cs:104-227` | 取消完成仍留已派生下一期 |
| A2–A5 | DB 上帝类 / DI 门面 / 双写路径 / 双窗口复制 | 全库结构 | 可维护性与测试性根因 |
| A6/D13/B10/S20 | 核心写路径/同步/任务服务零测试 | `TodoSidebar.Tests/` | 高风险无回归网 |

#### P2 中（加固与体验）

| ID | 标题 |
|---|---|
| D9 | 双回收站清理 API 口径不一致 |
| D10 | 时区/DateTimeKind 混用导致日桶偏移 |
| D11 | `MarkTaskSynced` 空时间戳跳过乐观锁 |
| D12 | 导入不 Normalize Recurrence；导出文化敏感 |
| D14 | `IncrementSettingCounter` 非原子 |
| D15 | 云端 `deleted_at` 为 text，本地为 DateTime |
| S8 | BindSyncId 失败仍用一次性 GUID 上传 |
| S9 | 部分 Delay/账号 HTTP 不可取消 |
| S10 | NetworkMonitor 只看网卡 |
| S11 | 上传预检不完整时可能盲覆盖 |
| S12 | 无分片 Upsert，大批次易整批失败 |
| S13 | EnsureUserScope 漏清 LastFullReconcile/Acct/头像 |
| S14 | 云端 avatar_data 无长度 CHECK |
| S15 | Supabase.Client Dispose 可能空操作 |
| S16 | 游标解析未锁 InvariantCulture |
| S17 | 本地头像读缓存不再校验 |
| U4 | TaskDetailDialog 先改内存再落库 |
| U5 | 多处同步 `Dispatcher.Invoke` 有死锁面 |
| U6 | 热键部分失败语义不清 |
| U7 | SoundService 事件/定时器清理不全 |
| U8 | Widget/Statistics 硬 `FindResource` 转换 |
| U9 | 任务列表虚拟化/阴影/1s 全树刷新成本 |
| U10 | 无边框对话框无障碍（Esc/自动化名） |
| B5 | `RecurringCompletedLifetime` 可刷且不回退 |
| B6 | 取消完成次日再完成重复发 XP |
| B7 | 打字 FlushNow 失败丢增量 |
| B8 | 每日任务路径静默丢弃 NLP DueDate |
| B9 | NLP「十一个半小时」等边界 |
| B11 | Direct→Pinyin 中途切换不 Flush |
| A7 | 单连接+全局信号量；SELECT * |
| A8 | README/版本/发布路径漂移 |
| A9 | 依赖偏旧；`UseWindowsForms` 疑似无用 |
| A10 | 安装器 AppId 占位/语言混杂/打包面过大 |
| A11 | 目录/命名空间/代码建窗不一致 |
| A12 | 大量空 catch / async void |
| A13 | 明文邮箱、start.bat 绝对路径 |

#### P3 低（卫生）

| ID | 标题 |
|---|---|
| D16–D20 | 迁移探测噪声、God 方法、全表解析、备份删除吞错、CreatedAt 默认 Now |
| S18–S19 | MD5 DeterministicGuid、密码显示框残留明文 |
| U11–U15 | 一次性定时器、Widget 1s 空转、模式切换双订阅、Hover 堆叠、热键反馈文案 |
| B12–B20 | 番茄中断记 1 分钟、CanExecute、7 日图口径、死字段、版本比较、API 变薄、月末规则、空 Deadline、挑战边界 |
| A14–A18 | sln 未含 tools、编码/gitattributes、website 重复、音频体积、过时注释 |

## [S2] Design

本轮只改行为正确性、安全卫生、数据一致性与关键性能/生命周期；大型结构重写拆成增量切片，不做一次性分层重写。

### 契约

1. **凭据（A1/S6/S5/U16）**
   - 仓库与发布白名单不得包含真实 `supabase.json` / Anon Key；`bin/publish_sc/` 进 `.gitignore`。
   - 「记住我」只持久化 email；升级时删除 `SavedPassword` 设置项。
   - 密码显示框在切换关闭/窗口关闭时清空。

2. **任务写路径（D1/D2/D5/D6/D11）**
   - 所有 Tasks 写入共用同一列清单与 `BindTaskParameters`。
   - `InsertTaskCore` 含 `CompletedAt`。
   - `ImportTasksUnique`：无 SyncId 行用稳定键（Title+CreatedAt+Deadline 哈希）生成或跳过并计数，禁止静默重复插入。
   - `UpsertTaskFromRemote` 的 UPDATE 必须 `WHERE Id = @id`。
   - `MarkTaskSynced`：`@expected` 非空时要求 `LocalUpdatedAt` 精确相等（NULL 视为不匹配）。

3. **同步（S1/S2/S3/S7/S8/S11/S12/S16）**
   - 下载 keyset：服务端条件 `(updated_at,id) > (cursor_at,cursor_id)`，客户端不再对同一页反复 `Range(0,N)` 空转。
   - 失败行计入 `minFailedUpdatedAt`，游标不得越过失败下界。
   - LWW 与内容比较统一 `ToUtc`；`TaskContentEquals` 覆盖 Estimated/Actual/DeletedAt。
   - `BindTaskSyncId` 失败则跳过该任务本轮上传。
   - 预检不完整的 ID 不上传；Upsert 按 50–100 行分片；`Task.Delay` 接受 `SyncToken`。
   - 游标解析使用 `InvariantCulture + RoundtripKind`。

4. **账号隔离与头像（S4/S13/S17）**
   - 头像缓存按 userId 命名；切号删除他人头像文件。
   - `kind==custom` 且无有效数据时返回 null。
   - 读本地缓存复用 `IsValidRemoteAvatar`。
   - `EnsureUserScope` 同步清理 `LastFullReconcileAt*`、`Acct*` 与头像文件。

5. **备份语义（D3/D4/D12）**
   - 导出包含成长相关表（至少完成记录/XP/番茄/挑战/打字/用户档案）或明确标注「仅任务」。
   - 恢复任务-only 备份时不得 orphan-purge 成长表。
   - CSV/MD 补全字段；导入 Normalize `Recurrence`；导出 `InvariantCulture`。

6. **打字统计（B1/B2/B7/B11）**
   - `Peek()` 无副作用；`Flush()` 只在 `TakeDelta`/分段切换。
   - `GetKeyboardLayout` 传 thread id。
   - 写库成功后才 `TakeDelta`，失败可回注。
   - Direct↔Pinyin 双向切换即 Flush。

7. **成长与循环（B3/B4/B5/B6）**
   - 抽取 `TodayProgressCalculator`，侧边栏与统计页共用；分母含今日已完成截止任务；比率 clamp。
   - 循环派生记 `SpawnedFromId`/锚点，存在任意存活实例则不重复派生；取消完成软删当次派生且未完成的下一期。
   - `RecurringCompletedLifetime` 与 XP 与「任务实例」对齐：取消完成回退；同实例不双发 `task_complete`。

8. **UI 生命周期（U1–U8/U10）**
   - 所有 HwndSource hook / ThemeChanged / MediaEnded / 一次性 Timer 在 Closed 对称释放。
   - `async void` 外壳 try/catch/finally；UI 封送优先 `BeginInvoke`/`CheckAccess`。
   - 资源查找 `TryFindResource` + 冻结回退。
   - 对话框 `IsCancel`/Esc。

9. **数据层性能与迁移（D7/D8/D9/D10）**
   - `CREATE INDEX IF NOT EXISTS` 覆盖 IsDeleted/Type、IsDirty、CompletedAt、DailyTaskCompletion.Date 等。
   - 所有云迁移脚本 `DROP TRIGGER IF EXISTS update_tasks_updated_at`。
   - 回收站清理单一 API，以 `DeletedAt` 为准。
   - 日桶/比较逐步改为归一化 UTC/本地 Date，不用裸 O 字符串字典序。

10. **测试网（D13/B10/S20/A6）**
    - 新增：导入去重/Recurrence、Upsert-by-Id、MarkTaskSynced 守卫、TaskContentEquals、LWW UTC、Peek 无副作用、TodayProgress、循环派生幂等、EnsureUserScope 表清单、Bind 失败跳过。
    - 使用临时 SQLite（`TODOSIDEBAR_TEST_DB`）或纯函数单测，禁止复制生产逻辑断言。

11. **结构债（A2–A5/A7 增量，非一次性重写）**
    - 抽出 `TaskSql`/`BindTaskParameters`、`TodayProgressCalculator`、共享账号头像行为，禁止扩大双写。
    - `TaskService` 改为依赖 `IDatabaseService`（接口补齐所需成员）。
    - 完整 DB 拆分/DI 去 Singleton/双 Window 合并记入 Journey，不在本轮强行完成。

### 错误与边界

- 同步单行/分片失败：记日志、不推进越过失败的游标、下轮重试；不整轮 abort 无日志。
- 导入报告跳过/丢弃行数，不静默。
- 禁止新的空 `catch { }`；已有处至少 `Debug.WriteLine`。

### 测试边界

- 单测覆盖纯逻辑与 SQLite 文件级集成；不测真实 Supabase。
- 不为测试开生产-only API。

## [S3] Out of Scope

- 一次性把 `DatabaseService` 拆成多仓库、全面去 `*.Instance`、合并 MainWindow/FullWindow（A2–A5 全量）。
- FTS5 搜索、音频转码、website 大改、安装器签名/正式 GUID 换号（仅做安全打包排除）。
- 云端表结构大迁移（`deleted_at`→timestamptz）以外的 Supabase 后端重设计。
- UI 视觉重设计、新功能。

## Tasks

- [x] T1: 凭据与发布卫生 — acceptance: `.gitignore` 含 `bin/publish_sc/`；安装脚本不打包真实 supabase.json；登录不再写入 `SavedPassword` 且升级清除旧键；密码显示框关闭时清空 (covers: A1,S6,S5,U16,S19)
- [x] T2: 任务写路径统一与导入/更新正确性 — acceptance: 共用列绑定；Insert 含 CompletedAt；无 SyncId 导入不重复；Upsert UPDATE 按 Id；MarkTaskSynced 精确守卫 (covers: D1,D2,D5,D6,D11; depends: T1)
- [x] T3: 同步 keyset/游标/LWW/内容比较 — acceptance: 真 keyset 条件；失败行游标下界；LWW/比较 ToUtc；TaskContentEquals 含耗时与 DeletedAt；Bind 失败跳过；预检不全不传；Upsert 分片；Delay 可取消；游标解析 Invariant (covers: S1,S2,S3,S7,S8,S9,S11,S12,S16; depends: T2)
- [x] T4: 头像与账号隔离 — acceptance: 头像按 userId；切号清理；custom 无数据返回 null；读缓存校验；EnsureUserScope 清 LastFullReconcile/Acct/头像 (covers: S4,S13,S17; depends: T1)
- [x] T5: 备份与导出完整性 — acceptance: JSON 备份含成长表或明确任务-only 且不 purge 成长；CSV/MD 字段全；导入 Normalize Recurrence；导出 InvariantCulture (covers: D3,D4,D12; depends: T2)
- [x] T6: SQLite 索引、清理口径与迁移 DROP TRIGGER — acceptance: 热路径索引存在；单一 purge 口径；v560/v571 脚本含 DROP TRIGGER (covers: D7,D8,D9,D10)
- [x] T7: 打字统计口径与持久化 — acceptance: Peek 无副作用；IME 用 tid；Flush 失败不丢量；Direct↔Pinyin 双向切换 (covers: B1,B2,B7,B11; depends: T2)
- [x] T8: 今日进度/循环派生/XP 诚实性 — acceptance: 共享 TodayProgress 且分母一致；循环幂等且取消完成收回派生；计数器/XP 不双发 (covers: B3,B4,B5,B6; depends: T2)
- [x] T9: UI 生命周期与异常外壳 — acceptance: hook/主题/音效/定时器对称释放；CheckUpdate 有 try/catch；Dispatcher 尽量 BeginInvoke；FindResource→TryFindResource；Esc/IsCancel (covers: U1,U2,U3,U5,U7,U8,U4,U10)
- [x] T10: 同步加固余量与网络探测 — acceptance: NetworkMonitor 轻量探测；avatar_data 长度 CHECK；Dispose 确认；绑定失败与账号 IO 取消 (covers: S10,S14,S15,S9; depends: T3)
- [x] T11: NLP 与每日任务入口 — acceptance: 十一/十二个半小时；每日任务不静默丢 DueDate；剩余 CanExecute 补齐 (covers: B8,B9,B13)
- [ ] T12: 核心回归测试网 — acceptance: 覆盖 T2/T3/T7/T8 关键纯逻辑与导入/Upsert/守卫；既有 181 测试仍绿 (covers: D13,B10,S20,A6; depends: T2,T3,T7,T8)
- [x] T13: 工程卫生与文档对齐 — acceptance: README 版本/结构/发布路径正确；UseWindowsForms 核实后移除或注明；空 catch 至少打日志；start.bat 相对路径 (covers: A8,A9,A12,A13,A18; depends: T1)
- [ ] T14: 中低优先级批量收尾 — acceptance: D14–D20、U6/U11–U15、B12/B14–B20、A7 批量 API/SELECT 列、A14–A17 可落地项完成或明确标为遗留 (covers: P2/P3 余量; depends: T2)
- [x] T15: 结构债增量切片 — acceptance: TaskSql/Bind 绑定器、TaskService→IDatabaseService、禁止新双写点；完整拆分记 Journey 不在本轮 (covers: A2–A5 增量; depends: T2)
