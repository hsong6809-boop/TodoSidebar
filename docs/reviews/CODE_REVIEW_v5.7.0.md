# TodoSidebar v5.7.0 代码审查报告

- **对象**：当前工作区代码（`TodoSidebar.csproj` Version=5.7.0）
- **规模**：主工程约 1.5 万行 C#/XAML；服务层 + 三态窗口 + Supabase 同步 + 成长系统
- **方法**：四路并行深审（数据层 / 云同步与鉴权 / UI 与生命周期 / ViewModel 与业务）+ 关键结论独立交叉复核
- **验证**：Debug 编译 0 警告 0 错误；单元测试 **146/146 通过**
- **对照**：仓库内已有 `CODE_REVIEW_v5.3.md`，本报告核验其历史问题修复情况，并报告 v5.5–v5.7 引入的新问题

---

## 总览

| 严重度 | 数量 | 说明 |
|---|---|---|
| 🔴 严重 | 5 | 云同步停摆、模式切换失步、数据串号、备份丢规则、重复任务双生 |
| 🟠 高 | 9 | 静默数据丢失窗、功能失效、口径矛盾、潜在崩溃 |
| 🟡 中 | ~15 | 工程加固、体验缺陷、资源生命周期 |
| 🟢 低 | ~20 | 边界条件、卫生问题 |

**最危险的共性主题（本版）**：
1. **防重入标志的早退路径不对称释放**（`SyncAsync`）
2. **统一入口被旁路**（模式切换未走 `App.SwitchDisplayMode`）
3. **账号隔离清单不完整**（`EnsureUserScope` 漏表）
4. **写路径列清单分叉**（导入缺 `Recurrence`；云模型缺耗时字段）

---

## 一、🔴 严重问题（建议立即修复）

### C1. `SyncAsync` 早退泄漏 `_syncInProgress` → 离线一次后云同步永久停摆

- **位置**：`Services/SyncService.cs:237-256`（早退）vs `:380-383`（finally 复位）
- **描述**：`Interlocked.CompareExchange` 占位后，两条早退路径**不在 try/finally 内**，不复位标记：
  - `:240-241` 未登录 → `return "未登录"`
  - `:244-255` 离线 → `return "已离线"`
- **影响**：启动/登录时无网，或 NetworkMonitor 短暂误报离线（见 M6），30s 循环首次 tick 即泄漏。此后 `SyncAsync` / 手动同步全部返回「正在同步中」，网络恢复也无法恢复，必须重启或登出。
- **复现**：断网启动 → 连网 → 点同步 → 永远报「正在同步中」。
- **修复**：把登录/离线检查包进与主路径同一 `try/finally`，或每个 `return` 前 `Interlocked.Exchange(ref _syncInProgress, 0)`。

### C2. 模式切换旁路 `App.SwitchDisplayMode`，`_currentMainWindow` 失步

- **位置**：
  - `MainWindow.xaml.cs:623-637`（ExpandFullMode_Click）
  - `FullWindow.xaml.cs:551-560`（CollapseToSidebar_Click）
  - `FullWindow.xaml.cs:937-950`（TodayAll_Click，「全部 ›」）
- **描述**：三处仍用旧模式 `new XxxWindow() → Show() → ReRegisterHotkeys → Close()`，**未**更新静态 `App._currentMainWindow`。而 v5.7 三态热键循环（`Ctrl+Alt+T`）读的是该静态引用。
- **影响**：
  1. 经「完整模式」按钮进入 FullWindow 后，热键仍认为当前是侧边栏 → 再 `new FullWindow()`，**双完整窗口**（旧窗成孤儿，事件未退订）。
  2. 「全部 ›」误判为 Full→Widget，直接弹悬浮球。
  3. `SwitchToWidget_Click` 已正确走统一入口，形成同文件内双轨实现。
- **修复**：三处改为 `App.SwitchDisplayMode(...)`，删除本地 `ReRegisterHotkeys`；`SwitchDisplayMode` 内 `current?.Close()` 包 try/catch，避免失败时连带关掉新窗口。

### C3. 导入路径丢失 `Recurrence` 列 → 备份恢复后重复任务全部退化为一次性

- **位置**：`Services/DatabaseService.cs:660-683`（`ImportTasksUnique`）
- **描述**：INSERT 列清单含 SyncId/IsDirty/DeletedAt/LocalUpdatedAt，**唯独缺 `Recurrence`**。同文件 `InsertTaskCore`/`ReplaceAllTasks`/`UpsertTaskFromRemote` 都写该列。
- **影响**：JSON 导入（`ExportService.ImportFromJson` → `ImportTasksUnique`）时，备份里的 daily/weekly/monthly 规则被静默丢弃。用户恢复备份后循环任务全部变成一次性任务。
- **修复**：INSERT 补 `Recurrence` + `@recurrence`。**结构性教训**：Tasks 写路径有 6 处 INSERT/UPDATE，加列必须同步全部。

### C4. 切号清库未清 `DailyTypingStat` → 旧账号打字量串入新账号云端

- **位置**：`Services/DatabaseService.cs:1632`（`EnsureUserScope` 清表列表）
- **描述**：清空 `Tasks, DailyTaskCompletion, XpLog, PomodoroSession, AchievementUnlocks, DailyChallenge, UserProfile`，**漏掉 `DailyTypingStat`**。该表登录后由 `SyncService.SyncTypingStatsAsync` 按日 LWW 上传。
- **影响**：用户 A 的每日击键/字数会在切到账号 B 后上传到 B 的 `typing_stat`。明确的多用户隔离漏洞。
- **修复**：清表列表加入 `DailyTypingStat`。**结构性教训**：`EnsureUserScope` 清表清单是账号隔离的单一事实源，新增可同步本地表必须同步加入。

### C5. 重复任务「完成 → 取消完成 → 再完成」再生成一期 → 幽灵实例

- **位置**：`Services/TaskService.cs:104-105`（CompleteTask→SpawnNextRecurrence）、`:191-209`（UncompleteTask）
- **描述**：完成截止任务无条件派生下一期；取消完成只把当前实例 `IsCompleted=false`，**已生成的下一期保留**。再次完成再派生一期。
- **影响**：误点完成再取消后，列表出现重复的下一期任务；同步后多设备都能看到。
- **修复**：派生前查「同 Recurrence + 预期 Deadline 的未完成存活任务」；或取消完成时级联删除当次派生实例；或用「已派生锚点日」做幂等。

---

## 二、🟠 高优先级问题

### H1. 首传无稳定 SyncId：上传成功但标记失败 → 云端重复任务

- **位置**：`Services/SyncService.cs:414-444`（每轮 `Guid.NewGuid()`）、`:495-518`（标记）
- **描述**：无 SyncId 的脏任务每轮生成新 GUID。云端 Upsert 成功后、`MarkTaskSynced` 写库前崩溃，下一轮再生成新 GUID → 云端孤儿重复行。XP/番茄已用 `DeterministicGuid`，任务主路径未对齐。
- **修复**：网络 IO 前先在本地事务写入 `SyncId=guid, IsDirty=1` 再上传；或对齐 DeterministicGuid。

### H2. 增量分页仍为 OFFSET（H2 部分残留）

- **位置**：`Services/SyncService.cs:564-592`
- **描述**：已加 `ORDER BY updated_at ASC` + 60s 重叠窗 + 24h 全量对账，但仍是 offset/limit。行被硬删/离开结果集时后续行左移，页末行被跳过；相同 `updated_at` 无次级排序键。
- **修复**：keyset 分页 `(updated_at, id)`。

### H3. 下载单行异常被吞且不入日志（M6 残留）

- **位置**：`Services/SyncService.cs:609-725`
- **描述**：`RecordObserved` 只记成功行（正确），但单行 catch 仅 `Debug.WriteLine`，不写 `_syncLog`。失败行若早于 `maxObserved−60s`，游标推进后永久漏同步（最长 24h）。
- **修复**：单行 catch 写 sync_log；游标取 `min(maxObserved−60s, minFailedUpdatedAt)`。

### H4. 云端 `avatar_data` 无大小/格式校验

- **位置**：`Services/AccountService.cs:173-184, 332-343`
- **描述**：上行已限 20MB+128px；下行 `ApplyRemote` 直接 `WriteAvatarFile`，无长度/PNG 魔数校验。
- **修复**：解码前限长度（如 ≤512KB）+ 校验 PNG 魔数。

### H5. 侧边栏与统计页「今日进度」分母口径不一致

- **位置**：`ViewModels/MainViewModel.cs:86-100` vs `StatisticsViewModel.cs:371-380`
- **描述**：
  | | 侧边栏 | 统计页 |
  |---|---|---|
  | 截止任务分母 | 仅今日到期未完成 | `deadline >= today` 且未完成（含未来任务） |
- **影响**：有下周到期的未完成任务时，两处完成率数字矛盾。
- **修复**：统计页 `pendingValidDeadlines` 改为 `== today`，或抽取共享 `CalculateTodayProgress()`。

### H6. NLP「两个半小时后」解析为 0.5 小时

- **位置**：`Services/NaturalLanguageParser.cs:48-53, 104`
- **描述**：`RelHoursRx` 第四分支 `半小时后` 会劫持「两个半小时后」的子串；`halfcn` 只认「一」。
- **影响**：「两个半小时后开会」被安排到 30 分钟后。
- **修复**：匹配前归一化中文数字（两/三…→ 2/3…），或扩展 `halfcn` 为 `[一二两三四五六七八九十]` 并映射。

### H7. 每日挑战进度在挑战未生成时静默丢失

- **位置**：`Services/DailyChallengeService.cs:82-118`
- **描述**：`RegisterProgress` 直接 `GetDailyChallenges(today)`，不生成；当日挑战为空时 foreach 空转，进度/XP 丢失。`GetTodayChallenges` 会生成，但 `RegisterProgress` 不调用它。
- **影响**：打开挑战 UI 之前完成的任务/番茄不计入挑战进度；跨天首任务更严重。
- **修复**：`RegisterProgress` 开头调用 `GetTodayChallenges()`。

### H8. `SaveNicknameAsync` 无 try/catch，async void 可崩进程

- **位置**：`AccountWindow.xaml.cs:192-212`
- **描述**：`await _account.SetNicknameAsync(...)` 全程无异常处理。同文件头像路径已修，昵称路径遗漏。
- **修复**：包 try/catch，失败时 `NicknameHint` 提示。

### H9. 热键注册失败无用户可见反馈（H5 残留）

- **位置**：`HotkeyService.cs:40-43, 61-94`；`App.xaml.cs:57-74`
- **描述**：`LastRegistrationFailed` 有属性、**全仓库无读取方**。热键被占用时静默失效。
- **修复**：`AttachHotkeysTo` 成功后检查并提示「部分全局热键注册失败」。

---

## 三、🟡 中等问题

| # | 问题 | 位置 | 要点 |
|---|---|---|---|
| M1 | `ImportTasksUnique` 把 `LocalUpdatedAt` 重写为导入时刻 | `DatabaseService.cs:683` | 抬高 LWW 基线，可能用旧导入覆盖较新编辑 |
| M2 | `GetTaskBySyncId` 无 ORDER BY | `DatabaseService.cs:1389-1404` | 软删+存活同 SyncId 并存时取行不确定 |
| M3 | 存量库 `init.sql` 的 `updated_at` 触发器仍击穿 LWW | `Database/init.sql:133-147` + 迁移脚本替换语句被注释 | 服务端 `now()` 改写客户端编辑时间 |
| M4 | 打字量时间解析未锁 InvariantCulture | `DatabaseService.cs:1299-1323` | 非 ISO 文化环境下 LWW 失真（半迁移残留） |
| M5 | 切号时 Settings 业务计数器未清 | `DatabaseService.cs:1632-1653` | `RecurringCompletedLifetime` 等污染新账号成就 |
| M6 | `NetworkMonitor` 只判断网卡可用 | `NetworkMonitor.cs:31,46` | 连 Wi-Fi 但无法达 supabase 仍判在线；与 C1 叠加触发永久停摆 |
| M7 | 「记住我」DPAPI 存密码原文密文 | `LoginWindow.xaml.cs:120-129` | 已有 session 恢复，额外存密码违反最小凭据原则 |
| M8 | 发布目录含真实 Anon Key | `bin/publish_sc/supabase.json` | gitignore 已挡仓库，但发布产物明文落盘 |
| M9 | 上传前预检 N+1 串行 GET | `SyncService.cs:414-438` | 100 脏任务=100 次往返，同步极慢 |
| M10 | `user_profile` TotalXp 相等时跳过合并 | `SyncService.cs:887-919` | 只涨连击不涨 XP 时云端连击停滞 |
| M11 | 手动「仅下载」不推进游标 | `SyncViewModel.cs:139` | 重复拉取；UI LastSyncTime 与真实游标不一致 |
| M12 | `TaskDetailDialog` 确认前就改共享 TaskItem | `TaskDetailDialog.xaml.cs:145-190` | 取消确认后内存与 DB 不一致 |
| M13 | `SupabaseClientService.Dispose` 不释放 Client | `SupabaseClientService.cs:77-83` | 只置 null，HttpClient/定时器泄漏 |
| M14 | 同步 HTTP 无 CancellationToken | `SyncService.cs` 多处 | `Stop()` 无法取消在途同步 |
| M15 | `SoundService` 退出不清理 MediaPlayer | `SoundService.cs` + `App.OnExit` | 与 OnExit 统一清理模式不一致 |
| M16 | `ThemeManager` 订阅 SystemEvents 未退订 | `ThemeManager.cs:113` | 关闭序列边缘风险 |
| M17 | 全局异常单次仍 `Handled=true` 静默 | `App.xaml.cs:380-411` | 熔断已加，单次错误用户无感知 |
| M18 | 无 DatabaseService/TaskService 测试 | `TodoSidebar.Tests/` | 高风险路径无回归网 |
| M19 | `UpdateTask` 不更新 `Type` | `DatabaseService.cs:491-507` | 若 UI 允许改类型则静默丢弃 |
| M20 | 云端 SyncTask 缺 EstimatedMinutes/ActualMinutes | `Models/SyncModels.cs` | 多设备预估耗时永远丢失 |

---

## 四、🟢 低优先级（摘要）

**解析/业务**
- NLP 不识别「凌晨/傍晚」；时间片段「25点」边界可再收紧
- 7 日图表仅含每日任务，不含截止任务（与顶部「今日完成」口径矛盾）
- Markdown 转义未覆盖 `*` `_`；多标签按 `#` 分割但存储是逗号
- MainViewModel 命令普遍无 CanExecute（点击无效但按钮仍可点）
- 云同步后 `UnlockService` 等级缓存未刷新（M-4）
- `ExportService` 内 `_taskService` 为死代码
- `QuickAddWindow` 绕过 `TaskService` 直接 `InsertTask`

**UI/资源**
- `MainWindow` 收起后 `_mouseCheckTimer` 150ms 空转
- `DwmBackdropHelper.GradientColor` 通道序仍可疑（0xAARRGGBB vs ABI 的 0xAABBGGRR）
- `NotificationWindow` 直接强转资源，主题损坏时构造即崩
- `LoginWindow.Topmost=True` 干扰用户操作
- `FullWindow.OnChallengesUpdated` 未封送 UI 线程
- `AnimationService` 每粒子 `new Random()`
- `WidgetWindow` 未订阅 ThemeChanged（亚克力 tint 不重算）

**工程**
- `IDatabaseService` 接口严重不完整；`TaskService` 依赖具体类
- `ExecuteLocked.Wait()` 无超时
- 导出/备份不含回收站任务；双轨回收站清理口径不一致
- `InsertImportedTask` 为死代码
- `UpdateChecker` 日期键未 InvariantCulture；无安装包签名校验

---

## 五、历史问题（v5.3）核验

| # | 历史问题 | 状态 | 证据 |
|---|---------|------|------|
| H1 | LWW 用上传时刻而非编辑时刻 | ✅ **已修复** | `SyncService.cs:466` 使用 `LocalUpdatedAt`；下载侧落库保留真实编辑时间 |
| H2 | 增量分页无 ORDER BY | ⚠️ **部分修复** | 已加 ORDER BY + 重叠窗 + 24h 对账；仍为 OFFSET（见 H2） |
| H3 | 登出失败假登出 | ✅ **已修复** | `AuthService.LogoutAsync:377-398` 本地清理与服务端解耦 |
| H4 | 热键登出失效 / 闭包持过期窗口 | ✅ **已修复** | `App.AttachHotkeysTo` + 静态 `_currentMainWindow`；但 C2 旁路又引入新失步 |
| H5 | 无单实例 / 热键失败无反馈 | ⚠️ **部分修复** | Mutex 已落地；失败反馈仍无（见 H9） |
| H6 | 过期通知死代码 | ✅ **已修复** | `GetDeadlineTasks(includeOverdue: true)` |
| H7 | NLP 不存在日期回落今天 | ✅ **已修复** | `SafeDate` 返回 null + 测试守护 |
| H8 | 数据库损坏重建被连接池击穿 | ✅ **已修复** | `ClearAllPools()` + 备份失败显式抛错 |
| M1 | 切号物理抹掉未同步脏数据 | ⚠️ **部分修复** | 有确认弹窗 + 会话恢复拒绝静默切号；确认后仍物理 DELETE，且漏清 DailyTypingStat（C4） |
| M2 | DeletedAt 多路径丢弃 | ✅ **已修复** | 全部写路径均写入 |
| M3 | MarkTaskSynced 丢 SyncId | ✅ **已修复** | 拆成绑 SyncId + 乐观守卫清脏 |
| M4 | Upsert TOCTOU | ⚠️ **部分修复** | 下载路径已用乐观守卫；上传首传仍有 H1 |
| M6 | 下载吞错但游标推进 | ⚠️ **部分修复** | 主体已修；单行失败残留（见 H3） |
| M7 | topmost 定时器泄漏 | ✅ **已修复** | Closing 中 Stop |
| M8 | FullWindow 拖拽误拖 | ✅ **已修复** | 未命中时 `_draggedTask = null` + finally |
| M9 | 全局异常吞掉 | ⚠️ **部分修复** | XamlParse 不吞 + 60s/5 次熔断；单次仍静默 |
| M10 | 12 小时制边界错误 | ✅ **已修复** | 中午/下午/晚上/上午完整 switch + 测试 |
| M11 | PendingTags 残留 | ✅ **已修复** | 先校验再改状态 |
| M14 | UpdateChecker tag 少于 3 段 | ✅ **已修复** | `FormatVersion` 按组件数 |
| M16 | user_profile 合并不对称 | ⚠️ **部分修复** | 已对称 max；TotalXp 相等时跳过（M10） |
| M17 | XP/番茄流水无幂等 | ✅ **已修复** | DeterministicGuid |
| M18 | 头像上行卡 UI | ⚠️ **部分修复** | 上行 Task.Run+缩放；下行无校验（H4） |
| M29/半迁移 | InvariantCulture | ⚠️ **大部分修复** | 打字量读端 2 处残留（M4） |

---

## 六、架构与工程评估

### 做得好的地方

- **安全基线扎实**：DPAPI 保护 session（CurrentUser + Entropy，失败不回退明文）；密码不落明文日志；Supabase 配置外置 + fail-fast；诊断日志只打 Key 指纹。
- **SQL 注入防护完整**：全部用户输入 `AddWithValue` 参数化；表名插值为硬编码字面量；LIKE 通配符转义。
- **锁模型正确**：`SemaphoreSlim(1,1)` + 统一 `*Core` 无锁内部方法，无重入死锁。
- **历史修复质量高**：v5.3 报告中 8/8 高严重度问题中 5 项完全修复、3 项主体修复并留有明确残留窗；修复处普遍有 `R##/审查 ##` 注释可追溯。
- **测试有回归意识**：NLP、Recurrence、DPAPI、License 等关键纯逻辑有测试；146 用例全绿。
- **单实例、热键迁移、异常熔断** 三项 App 级防护已到位。

### 主要架构风险

1. **「统一入口被旁路」**：`App.SwitchDisplayMode` 是 v5.7 三态形态的唯一正确切换入口，但 3 处旧写法未迁移（C2）。同类模式：`RegisterProgress` 依赖 `GetTodayChallenges` 的副作用却不调用（H7）。
2. **写路径列清单分叉**：Tasks 表 6 处 INSERT/UPDATE，加列必须同步（C3 已中招）；云模型 `SyncTask` 是字段同步瓶颈，本地加列不会自动上云（M20）。
3. **账号隔离清单不完整**：`EnsureUserScope` 是单一事实源，漏表即串号（C4）。
4. **防重入标志**：占用后必须在所有路径（含早退）对称释放（C1）。
5. **双实现口径**：侧边栏与统计页各自算「今日进度」（H5）；回收站清理有两条 API（M）。
6. **测试缺口集中在高风险路径**：DatabaseService / SyncService / MainViewModel / ExportService 零测试；历史修复容易被无声破坏。

---

## 七、修复优先级建议

### P0 — 立即（阻断性）

1. **C1** `SyncAsync` 早退复位 `_syncInProgress`（一行级改动，影响面最大）
2. **C2** 三处模式切换改走 `App.SwitchDisplayMode`
3. **C3** `ImportTasksUnique` 补 `Recurrence` 列
4. **C4** `EnsureUserScope` 清表列表加入 `DailyTypingStat`

### P1 — 本迭代

5. **C5** 重复任务派生幂等
6. **H1** 首传 SyncId 预绑定
7. **H5/H6/H7/H8** 今日进度对齐 / 两个半小时 / 挑战进度 / 昵称异常
8. **H2/H3** keyset 分页 + 失败行游标下界

### P2 — 下迭代

9. H4/H9、M1–M14 中与数据一致性/安全相关的项
10. 补 DatabaseService / ExportService / MainViewModel 集成测试

### P3 — 持续

11. 中低优先级体验与卫生问题
12. 建立「加列检查清单」：本地表 6 处写路径 + `EnsureUserScope` 清表 + `SyncTask` 模型 + 云端 SQL

---

## 八、审查方法说明

- 四路子代理分别完整通读数据层、同步鉴权、UI 生命周期、ViewModel/业务服务后独立出报告
- 主代理对 C1/C2/C3/C4/C5/H5/H6/H7/H8 等关键结论逐条回读源码交叉核验
- 编译与测试在本机实际执行（`dotnet build --no-restore` + `dotnet test --no-restore`）
- 未修改任何生产代码；本报告为只读审查产物

---

*报告生成时间：2026-09-11 | 审查基线：工作区 HEAD（v5.7.0）*
