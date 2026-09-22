# TodoSidebar v5.3.0 代码审查报告

- **对象**：commit `38ac08f`（v5.3.0），全部 C#/XAML 源码约 15,000 行
- **方法**：五路并行深审（数据库层 / 云同步与认证安全 / 核心业务逻辑 / UI code-behind / v5.x 新功能）+ 关键结论独立交叉复核
- **交叉验证**：Debug 编译 0 警告 0 错误；单元测试 104/104 通过（即下述缺陷均无测试守护）

## 总览

| 严重度 | 数量 | 说明 |
|---|---|---|
| 🔴 高 | 8（含 1 疑似） | 静默数据丢失、安全/隐私、核心功能失效 |
| 🟠 中 | 19（含 1 疑似） | 数据一致性、功能缺陷、资源泄漏 |
| 🟢 低 | ~30 | 边界条件、体验、卫生问题 |

最危险的共性主题：**「回收站 × 云同步」交互**与**「时间戳/游标语义」**，两者共同构成当前最大的静默数据丢失面。

---

## 🔴 高严重度

### H1. LWW 用「上传时刻」而非「编辑时刻」判定冲突 —— 离线编辑覆盖他机新修改
- **位置**：`Services/SyncService.cs:321`（上传盖时间戳）、`:461`（比较）
- 上传时 `UpdatedAt = DateTime.UtcNow` 取"现在"，而非任务真实编辑时间 `LocalUpdatedAt`；下载侧用远端该值与本地编辑时间比大小。
- **影响**：设备 A 离线编辑 2 小时后上线，其修改被当成"最新"；期间设备 B 更晚的真实编辑被静默覆盖。「谁最后联网谁赢」取代「谁最后编辑谁赢」，远程胜出无任何提示。
- **修复**：上传携带 `task.LocalUpdatedAt ?? DateTime.UtcNow`。

### H2. 增量分页拉取无 ORDER BY，翻页漂移漏行且被游标永久化
- **位置**：`Services/SyncService.cs:399-420`（无 OrderBy 的 Range 分页）、`:235-238`（游标推进为墙钟）、`:408`
- PostgREST 无排序时行序未定义；>500 条必翻页，两页之间远端有写入即页界位移跳行。跳过的行落在旧新游标之间，下次 `>= 新游标` 过滤永远不再拉取。
- **影响**：该设备**永久**缺少这些云端任务，静默数据丢失。`:419` 的 50000 行上限亦是无提示截断。
- **修复**：`.Order("updated_at", Ascending)` + id tie-breaker 或 keyset 分页；游标改取本次观测最大值并回退重叠窗口（60s），配合已有回声跳过。

### H3. 登出失败被吞 → 「假登出」：凭据保留、重启自动复活登录
- **位置**：`Services/AuthService.cs:359-374`；调用方 `MainWindow.xaml.cs:663-688`、`FullWindow.xaml.cs:570-581`
- `LogoutAsync()` 内部 catch 后仅 Debug.WriteLine；UI 无条件按已登出处理。SignOut 网络失败时：refresh token 未吊销、`CurrentUser` 未清、DPAPI 加密的 session.json 未删除，下次启动自动恢复登录进入原账号。共享电脑场景属真实隐私风险。
- **修复**：本地清理（清用户态+删 session 文件）与服务端吊销解耦；失败也要清本地并向用户如实提示。

### H4. 登出→重登后全局热键永久失效；热键处理器持过期窗口引用可致双主窗口
- **位置**：`App.xaml.cs:125-153`；`LoginWindow.xaml.cs:251-253`（已独立复核：`RegisterHotkeys` 仅 OnStartup 调用一次）
- 热键注册在启动窗口 HWND 上；登出 Close 销毁后无人重注册。`ToggleSidebarRequested` 处理器闭包捕获局部 `mainWindow`，此后按键基于过期引用判断，可能再开一个 FullWindow 而不关当前窗口。
- **修复**：收敛为 `App.SwitchMainWindow(Window)`；登出注销热键、登录成功统一走该方法；处理器读静态"当前主窗口"属性而非闭包。

### H5. 完全没有单实例保护；热键注册失败零反馈
- **位置**：`App.xaml.cs:44-170`（已独立复核：全仓库无 Mutex）；`Services/HotkeyService.cs:63-81`
- 双开 = 两进程同开同一 SQLite 库、通知/同步/每日检查翻倍；第二实例三个全局热键静默全灭。M29 引入的 `LastRegistrationFailed`（:42/:85）只写不读。
- **修复**：OnStartup 前置命名 Mutex（`Local\TodoSidebar.SingleInstance`）；主窗口加载后消费 `LastRegistrationFailed` 给一次性提示。

### H6. 「🔴 任务已过期」通知分支不可达 —— 过期提醒功能整体静默失效
- **位置**：`Services/NotificationService.cs:94-101`；根因 `Services/TaskService.cs:36-38`、`Models/TaskItem.cs:186`
- 通知数据源 `GetDeadlineTasks()` 已过滤 `Deadline.Date < today` 的任务，保留下来的任务恒有 `DeadlineEndOfDay > Now`，`timeLeft <= 0` 永假——死代码。
- **修复**：过期检查改用不过滤日期的独立查询，保留 `_notifiedTasks` 去重防重复弹窗。

### H7. 自然语言解析对不存在日期静默回落「今天」并落库
- **位置**：`Services/NaturalLanguageParser.cs:199-203`（`catch { return DateTime.Today; }`）、`:148-156`
- 「4月31日」「2月30日」「13月1日」等笔误全部解析成今天到期，通过校验落库，触发今日通知、计入统计，用户极难察觉。
- **修复**：`SafeDate` 失败返回 null 让 DueDate 保持空（或月末钳制如 4/31→4/30），绝不回落 Today。

### H8.【疑似】数据库损坏重建路径会被默认连接池击穿
- **位置**：`Services/DatabaseService.cs:76-99`、`:203-248`、连接串 `:67` 无 `Pooling=False`；Microsoft.Data.Sqlite 8.0 默认启用池
- 损坏在打开后的首条命令才暴露时：Dispose 只是归还池（句柄未真关）→ `File.Delete` 面对仍被池持有的文件失败或 delete-pending → 新建同字符串连接直接取回坏 session，「重建」未发生，后续写入幽灵文件、退出即丢。
- **修复**：重建前 `SqliteConnection.ClearAllPools()`；先 checkpoint 再备份/删除；删除失败不得继续假重建。

---

## 🟠 中严重度

| # | 问题 | 位置 | 要点 |
|---|---|---|---|
| M1 | 切号清库物理抹掉未上传墓碑与脏数据 | `DatabaseService.cs:1318-1327` | `DELETE FROM Tasks` 不区分 IsDirty；离线工作永久丢失；切回原账号后云端已删任务整批复活。修：清前查脏行数给出口/隔离区 |
| M2 | DeletedAt 在 4 条写入路径系统性丢弃 | `DatabaseService.cs:1148-1164/1186-1187/405/1245` | 同步层构造了该值但 SQL 不写列 → 远端墓碑落地 DeletedAt=NULL，清理守卫永不命中，清理口径分裂 |
| M3 | MarkTaskSynced 守卫失败连 SyncId 一并丢 | `DatabaseService.cs:1100-1107` + `SyncService.cs:303` | 上传成功与标记间编辑 → 下轮换新 GUID 云端再造一行 → 本地重复任务 |
| M4 | UpsertTaskFromRemote 判定与写入非原子（TOCTOU） | `SyncService.cs:455-500` + `DatabaseService.cs:1161` | LWW 在锁外判定、写入无条件 `IsDirty=0` → 并发编辑被覆盖且永久丢失 |
| M5 | 导入去重把回收站同源任务当"已存在" | `DatabaseService.cs:589-591/1124` | 注释称只看存活行，实际含软删行 → 导入缺数据无提示 |
| M6 | 下载逐条吞错但游标照常推进 | `SyncService.cs:502-505/235-238` | 出错行 UpdatedAt 落入已越过区间 → 永久漏同步 |
| M7 | `_topmostTimer` 关窗不停 → 每次模式切换泄漏整个 MainWindow | `MainWindow.xaml.cs:497-502`（Closing 清单缺它 :160-167） | Tick 闭包持 this 致无法 GC；每 3 秒对已销毁 HWND 调 SetWindowPos。附带缺口：收起态 `ReAssertTopmost` 直接 return（:525），触发条可能被全屏应用盖住致悬停展开失效 |
| M8 | FullWindow 漏移植 M25 拖拽修复 | `FullWindow.xaml.cs:994-1024` 对照 `MainWindow.xaml.cs:1002-1040` | 点过任务 A 后在空白处按下拖动会误拖 A 执行重排；DoDragDrop 无 finally 兜底 |
| M9 | 全局 UI 异常一律 `e.Handled=true` 吞掉 | `App.xaml.cs:212-220` | 僵尸态运行；async void 处理器异常全进黑洞。修：连续异常熔断 + 致命类型不标记 Handled |
| M10 | 12 小时制边界错误 | `NaturalLanguageParser.cs:206-222` | 「晚上12点」→中午12:00、「中午1点」→凌晨01:00，差 12 小时，连带按时完成 XP 判定错位 |
| M11 | 校验失败提前 return 后 PendingTags 残留 | `MainViewModel.cs:429-439` 对照 `:392-400` | 上次输入的 #标签 污染下一个不相干的新任务 |
| M12 | yyyy-MM-dd 格式化 InvariantCulture 半迁移 | 写端 `TaskService.cs:78` 已锁；读端 `DatabaseService.cs:833/853/900/947/1510/1849`、`StatisticsViewModel.cs:98/166/211/221/242` 未锁 | 泰历/回历环境每日完成、连击、热力图整体错位归零 |
| M13 | 时间片段「先剥离、后校验」 | `NaturalLanguageParser.cs:159-166` | 「25点开会」→标题被删字、无时间无提示 |
| M14 | Release tag 少于 3 段时更新检测抛异常静默失效 | `UpdateChecker.cs:103`（`ToString(3)`） | tag 为 `v5.4` 这类两段式时每日检测永远失败。修：按组件数格式化或直接 `ToString()` |
| M15 | 主 SQL 脚本缺 deleted_at 列的部署耦合 | `supabase_tasks_rls.sql:22-38` vs `SyncModels.cs:59-60` | 新环境软删任务批量上传必失败、反复重试、"已删复活"。修：主脚本补列 + `add column if not exists` |
| M16 | user_profile 合并不对称丢连击天数 | `SyncService.cs:658-684` | 远端胜取 Max、本地胜整行覆盖 → 连击 30 天被连击 2 天的设备覆盖 |
| M17 | XP 流水/番茄会话上传无幂等性 | `SyncModels.cs:70/98`、`SyncService.cs:597-627` | 每轮新 GUID，upsert 退化为 INSERT；崩溃窗口内重传造成云端重复流水 |
| M18 | 自定义头像 UI 线程同步解码最大 20MB 图 | `AccountService.cs:255-266/354-390`、`AccountWindow.xaml.cs:142-165` | 方法名带 Async 实为同步，大图秒级冻结界面。修：ProcessImageToBase64 移入 Task.Run（Freeze 后回传） |
| M19 |【疑似】登录处理器后台化与立即建主窗口竞态 | `AuthService.cs:264-286`、`LoginWindow.xaml.cs:244-255` | EnsureUserScope 清库与新 MainViewModel 读库并行，切号瞬间可能闪现上一账号数据。修：登录路径先等归属就绪 |

## 🟢 低严重度（摘要）

**解析/业务**
- 「一个半小时后」算成 0.5h（NLP :48-89）；「M月d日」同月已过日期不折明年必然被拦（:152，两个 agent 独立发现）；「明天周三」同现时"明天"残留标题（:105-142）
- 成就「今日完胜」用当前任务数判定全清（AchievementService.cs:102/164）；7 日统计历史分母用今天的任务数（StatisticsViewModel.cs:234-256）；今日进度环分母漏逾期任务（MainViewModel.cs:79-85）；补结算跨多天断连横幅可能不触发（LevelService.cs:194-212，疑似）
- CompleteTaskCommand 无已完成防护（MainViewModel.cs:450-456）；MainViewModel 的 TaskService 没接 MessageService，完成失败无提示（:122）；Dispose 漏停 `_undoTimer`（:692-706）；每次完成任务 UI 线程最多 ~365 次逐日查询（StatisticsViewModel.cs:205-229）；拖拽排序残留并列 SortOrder（MainViewModel.cs:666-690，疑似）

**UI**
- 亚克力 GradientColor R/B 通道颠倒（DwmBackdropHelper.cs:87，默认关所以没人发现）；番茄钟停止确认框无 owner 弹出瞬间侧边栏收起（MainWindow.xaml.cs:411）；详情对话框保存把截止时间成分抹零并误标有修改（TaskDetailDialog.xaml.cs:54-57/181-185，疑似）；收起态触发条命中宽度是物理像素常量高 DPI 偏窄（MainWindow.xaml.cs:22/559-565）；并存两个签名不同的 SetWindowPos P/Invoke 死代码（:1212-1216）

**导出/账号**
- Markdown 导出丢弃"已完成但无 CompletedAt"的任务（ExportService.cs:69-88）；转义不足（反斜杠结尾/多行撕裂列表 :123-125）；MD 异常路径临时文件泄漏、JSON 固定 .tmp 名互踩（:42/:99-104）；导入备份绕过 ThemeManager 直写设置，强调色重启才生效、易被误判为 V5.1 回弹复发（:217-222/276-281，经核验 V5.1 修复本身干净）
- 昵称保存失败完全静默（AccountWindow.xaml.cs:181-189 + App 全局吞噬）；头像缓存为全局单一路径存在跨账号串号窗口（AccountService.cs:29-31，疑似）；统计页停留时热力图/折线颜色不随主题即时刷新（HeatmapLevelToBrushConverter.cs:28-29）

**同步/安全卫生**
- 认证超时后底层 SignIn 继续完成成幽灵会话（AuthService.cs:95，疑似）；AnonKey 前 24 字符+URL 写明文诊断日志、旧 anon key 曾进 git 历史（LoginWindow.xaml.cs:395-411，建议确认已轮换）；Stop 先 Dispose PeriodicTimer 且循环只捕 OCE（SyncService.cs:151/165/739-746）；Supabase 客户端无 HTTP 超时、CT 不传递，单请求可挂 ~100s（SupabaseClientService.cs:64-72）；SavedEmail 明文落盘、诊断日志无轮转；InitializeAsync 防重入非线程安全（SyncService.cs:94，疑似）
- DeletedAt 用 DateTime.Now 本地时区入库上云，全库唯一非 UTC 时间戳（DatabaseService.cs:472 + SyncService.cs:323，与全链路 UTC 约定不一致——本项为主审独立抽查实锤）

**数据库**
- 旧清理路径 PurgeDeletedTasks 无事务、不清子表孤儿（DatabaseService.cs:647-672）；日期 ISO 文本直接比较/截取跨时区迁移错位（:706-716/921-926/988）；Initialize() public 可重入双连接（:62-64）；Dispose 与在途操作竞态（:1869-1877）；枚举强转无校验（:734-735）；纯同步 API + UI 直调的冻结风险（架构级）

---

## ✅ 重点排查确认无问题（避免误报）

- **SQL 注入**：全参数化，动态表名/列名均为硬编码白名单；LIKE 通配符正确转义
- **Token 存储**：session.json DPAPI(CurrentUser+Entropy)、失败拒落盘、原子写、拒绝明文通道；密码同样 DPAPI；token/密码不入日志；冲突/邮箱日志已脱敏
- **RLS**：五表策略齐全（前提：迁移脚本被执行，见 M15）；无硬编码密钥
- **版本比较**：System.Version 数值比较，无 "0.10<0.9" 问题（唯一问题是 M14 的格式化）
- **线程安全**：ObservableCollection 全部 UI 线程变更；信号量重入已规避；TryRewardXp 单锁单事务原子
- **事件/GC**：窗口 OnClosed 对长生命周期事件成对退订；头像加载 OnLoad+Freeze 不锁文件；退出清理链完整
- **热力图布局**：周一制首周补格、105px 对齐、阈值与测试吻合、翻年边界一致
- **导出编码**：UTF-8 BOM、原子替换、CSV 公式注入已缓解

---

## 建议修复顺序

1. **H1+H2(+M6/M1 游标族)**：同一根因（时间戳/游标语义），一次改动消除两类静默数据丢失
2. **H3 假登出**（小改动，安全收益大）→ **H6 过期通知死分支 + H7 日期回落**（各几行）
3. **H4+H5 热键生命周期重构 + 单实例锁**（改动小收益大）→ **M7 一行 Stop()** → **M8 移植现成补丁**
4. **M1 切号清库防护** → **M2/M3 回收站×同步列补齐与守卫拆分** → **M15 合并一段 SQL**
5. 其余中低项随迭代；**M14 一行修复**建议顺手带上

---
*五路审查由独立子代理完成，高危结论经主审二次抽查源码核实（热键单次注册、无 Mutex、LastRegistrationFailed 未消费、DateTime.Now/UtcNow 混用、NotificationService 死分支均已独立确认）。*

---

# 修复记录（审查后同日完成，R 编号与代码注释对应）

**验证状态：Debug 编译 0 警告 0 错误；单元测试 109/109 通过（含新增 5 个解析器回归测试）。**

## 高危（8/8 全部处理）

| R# | 对应 | 修复内容 |
|---|---|---|
| R1+R3+R4 | H1/H2(+M1/M6) | 上传携带真实编辑时间；分页加 `updated_at` 稳定排序；游标改取"成功行最大 updated_at −60s 重叠窗"；每账号每 24h 自动全量对账兜底收敛 |
| R19 | H3 | 登出本地清理与服务端解耦——失败也清用户态+删 session 文件并返回结果供 UI 提示 |
| R41 | H4 | 静态当前主窗口引用 + `AttachHotkeysTo/DetachHotkeys`；重登后热键重新注册到新窗口 |
| R40 | H5 | 命名 Mutex 单实例保护；双开提示退出 |
| R24 | H6 | `GetDeadlineTasks(includeOverdue:true)` 作为通知源，过期提醒复活 |
| R25 | H7 | `SafeDate` 失败返回 null（DueDate 保持空），不再回落「今天」 |
| R15 | H8 | 损坏重建前 `ClearAllPools()`；删除失败时中止"假重建"显式报错 |

## 中危（修复 16 / 缓释或挂起 3）

R2(稳定排序) R5(profile 合并对称取 Max) R6(Stop ODE) R7(InitializeAsync 原子防重入) R8(UpsertTaskFromRemote 乐观守卫返回 bool) R10(XP/番茄确定性 GUID 幂等) R11(MarkTaskSynced 拆分绑定与清脏) R12(DeletedAt 补齐全部写入路径) R13(导入去重排除软删行) R14(PurgeDeletedTasks 事务化) R17(DatabaseService.Initialize CAS) R18(Dispose 先等锁) R22(登录同步等待 EnsureUserScope) R23(头像解码移入线程池) R33(PendingTags 先校验后赋值) R38/R39(统计分母口径+批量查询替代逐日打库+InvariantCulture 补齐)

> M1 切号清库防护、M9 全局异常熔断已做基础版（熔断阈值 60s×5 次 + XamlParseException 不吞）；M19 登录竞态由 R22 同步化消除。

## 低危（修复 ~20 项）

R9(deleted_at 统一 UTC+展示转本地) R16(枚举钳制) R20(超时幽灵会话清理) R21(anon key 日志改指纹) R26-R31(NLP 六处边界) R32(TaskService 接 MessageService) R34(重复完成防护) R35(_undoTimer 释放) R36(断连横幅) R37(成就今日完胜口径) R42(异常熔断) R43(topmostTimer 关闭停止) R44(收起态保持置顶) R45(确认框 owner ×2) R46(DPI 触发条宽度) R47(死代码 P/Invoke 删除) R48(FullWindow 拖拽 M25 移植+finally) R49(截止日期 .Date 口径) R50(亚克力 R/B 修正) R51(版本格式化按组件数) R52(RLS 脚本补 deleted_at) R53(导出临时文件清理+GUID 名) R54(MD 导出不再丢行) R55(MD 转义加强) R56(恢复备份立即应用外观)

## 未处理（需产品决策或后续迭代）

- ~~M1 切号清库确认弹窗~~ → **已补完（R57–R59，见下方补完记录）**
- ~~进度环分母口径 L10~~ → **已补完（R60：逾期任务计入分母，列表展示行为不变）**
- 头像缓存跨账号残留(L7 疑似)、统计页颜色陈旧(v5.x-L5)：影响极小
- 架构级"纯同步 DB API 在 UI 线程直调"(DB-L6)：建议下个版本做异步服务改造
- 云端历史 anon key 轮换确认 + Supabase 控制台重跑 `supabase_tasks_rls.sql`：需后台操作

### 补完记录（二轮）

| R# | 对应 | 修复内容 |
|---|---|---|
| R57 | M1 前置 | 新增 `DatabaseService.GetDirtyTaskCount()`（含接口） |
| R58 | M1 时序 | 归属校验（EnsureUserScope）移交 LoginWindow 在登录路径上同步执行；App 后台登录处理器不再清库——消除"确认弹窗还没弹、库已被后台处理器清掉"的时序漏洞 |
| R59 | M1 启动路径 | 会话自动恢复遇到"恢复账号 ≠ 本地数据归属 且 存在未上云脏数据"时不再静默清库：转为本机登出，由登录窗口的确认弹窗显式决策 |
| — | M1 主路径 | LoginWindow 检测到切号且 dirty>0 时弹确认框（列出将丢失的数据类型，建议先回原账号同步）；取消则登出刚认证的新账号留在登录页 |
| R60 | L10 | 今日进度环分母计入逾期未完成截止任务（口径：完成前始终视为待办）；「截止任务」列表展示口径保持不变 |

**补完后状态：编译 0 警告 0 错误；109/109 测试通过。中危 19/19 全部闭环。**

**改动文件清单**：SyncService.cs、DatabaseService.cs、AuthService.cs、AccountService.cs、NaturalLanguageParser.cs、TaskService.cs、NotificationService.cs、LevelService.cs、AchievementService.cs、ExportService.cs、ThemeManager.cs(未改，核验干净)、DwmBackdropHelper.cs、UpdateChecker.cs、App.xaml.cs、LoginWindow.xaml.cs、MainWindow.xaml.cs、FullWindow.xaml.cs、TaskDetailDialog.xaml.cs、MainViewModel.cs、StatisticsViewModel.cs、SyncViewModel.cs、Models/TaskItem.cs、Interfaces/{IAuthService,IDatabaseService,ISyncService,ITaskService}.cs、supabase_tasks_rls.sql、TodoSidebar.Tests/NaturalLanguageParserTests.cs
