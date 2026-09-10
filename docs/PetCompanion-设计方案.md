# 桌宠伴侣 (PetCompanion) 设计方案

> 为 TodoSidebar 打造的智能桌面宠物，通过大模型驱动实现情感陪伴、任务提醒与智能对话。

---

## 一、总体架构

### 1.1 技术选型

| 维度 | 决策 | 理由 |
|------|------|------|
| 框架 | WPF (.NET 8.0) | 与 TodoSidebar 同栈，共享 Models/Services，降低集成成本 |
| 进程模型 | 独立进程 | 桌宠崩溃不影响 Todo 主程序；可独立升级 |
| IPC 通道 | Named Pipes + SQLite 共享 | Pipes 做实时事件推送，SQLite 做数据读取（已启用 WAL） |
| LLM 接入 | OpenAI 兼容 API + Ollama 本地 | 用户自选云端/本地，API Key 本地加密存储 |
| 动画 | WPF Storyboard + 帧动画 Sprite | 轻量、无额外依赖；复杂特效可用 Lottie-Windows |
| 窗口 | 无边框透明置顶窗口 | 桌宠悬浮于桌面，不抢焦点可选 |

### 1.2 进程拓扑

```
┌─────────────────────┐         Named Pipes          ┌─────────────────────┐
│   TodoSidebar.exe   │◄──────(双向事件通道)────────►│  PetCompanion.exe   │
│   (主程序)           │                              │  (桌宠进程)          │
│                     │                              │                     │
│  TaskService        │         SQLite WAL           │  PetBrainService    │
│  LevelService       │◄──────(共享 %APPDATA%\       │  LlmService         │
│  PomodoroService    │        TodoSidebar\todo.db)  │  AnimationEngine    │
│  NotificationService│                              │  DialogueManager    │
│  ...                │                              │  ...                │
└─────────────────────┘                              └─────────────────────┘
```

### 1.3 通信协议

**Named Pipe 名称：** `\\.\pipe\TodoSidebar-Pet`

消息格式（JSON）：

```json
{
  "type": "event|command|query|response",
  "action": "task.completed|level.up|chat.send|pet.mood|...",
  "payload": { ... },
  "timestamp": "2026-01-15T10:30:00Z",
  "id": "uuid"
}
```

**事件方向：**

| 方向 | 事件 | 说明 |
|------|------|------|
| Todo → Pet | `task.completed` | 任务完成，触发庆祝 |
| Todo → Pet | `task.overdue` | 任务逾期，触发提醒 |
| Todo → Pet | `level.up` | 升级，触发特效 |
| Todo → Pet | `combo.broken` | 连击断裂，触发安慰 |
| Todo → Pet | `combo.milestone` | 连击里程碑(7/30/100) |
| Todo → Pet | `pomodoro.start` | 番茄开始，进入专注陪伴 |
| Todo → Pet | `pomodoro.end` | 番茄结束 |
| Todo → Pet | `achievement.unlocked` | 成就解锁 |
| Todo → Pet | `daily.reset` | 新的一天 |
| Todo → Pet | `challenge.completed` | 每日挑战完成 |
| Pet → Todo | `chat.message` | 用户对桌宠说话 |
| Pet → Todo | `task.create` | 桌宠代建任务 |
| Pet → Todo | `task.complete` | 桌宠代完任务 |
| Pet → Todo | `app.open` | 请求打开主窗口 |
| Pet → Todo | `focus.start` | 请求开始番茄 |
| 双向 | `heartbeat` | 存活检测，每 5s |
| Pet → Todo | `query.tasks` | 查询今日任务摘要 |
| Pet → Todo | `query.stats` | 查询统计数据 |

---

## 二、桌宠本体设计

### 2.1 角色设定

**初始角色：「小待」(XiaoDai)**

- 外形：圆润的方形小生物，头顶一根会动的呆毛（天线），眼睛大而有神
- 性格：元气、有点话痨、偶尔犯懒、认真起来很可靠
- 配色：与 TodoSidebar 主题联动（Indigo-500 为主色）

**备选角色（后续扩展）：**

| 角色 | 性格 | 特点 |
|------|------|------|
| 小待 | 元气话痨 | 默认，全能型 |
| 阿墨 | 高冷学霸 | 话少但句句到点，专注模式时特别安静 |
| 团子 | 软萌治愈 | 擅长安抚情绪，连击断裂时安慰效果拉满 |
| 机械姬 | 极简科技 | 数据可视化风格，用图表说话 |

### 2.2 状态机与情绪系统

```
                    ┌──────────┐
         ┌─────────│  Idle    │─────────┐
         │         │ (待机)    │         │
         ▼         └──────────┘         ▼
    ┌─────────┐                   ┌──────────┐
    │  Happy  │◄──────────────────│  Working │
    │ (开心)   │   任务完成/升级     │ (工作中)  │
    └─────────┘                   └──────────┘
         ▲                              │
         │                              ▼
    ┌─────────┐    连击断裂/逾期    ┌──────────┐
    │Focused  │◄──────────────────│  Sad     │
    │ (专注)   │                   │ (难过)    │
    └─────────┘                   └──────────┘
         │                              │
         ▼                              ▼
    ┌─────────┐                   ┌──────────┐
    │  Sleep  │◄──────────────────│  Tired   │
    │ (睡觉)   │   深夜/长时间无操作  │ (疲惫)    │
    └─────────┘                   └──────────┘
```

**情绪值（Mood Value）：** 0~100 连续值，映射到离散状态：

| 区间 | 状态 | 动画 | 触发条件 |
|------|------|------|----------|
| 80-100 | Happy | 跳跃、转圈 | 任务完成、升级、高连击 |
| 60-79 | Idle | 眨眼、晃动 | 默认状态 |
| 40-59 | Working | 打字、看书 | 番茄专注中 |
| 20-39 | Tired | 打哈欠 | 工作日深夜、连续工作>3h |
| 0-19 | Sad | 蹲下、垂耳 | 连击断裂、任务大量逾期 |
| 特殊 | Focused | 静坐冥想 | 番茄专注模式 |
| 特殊 | Sleep | Zzz | 23:00-7:00 或用户设置 |

### 2.3 动画清单

| 动画名 | 时长 | 触发 | 描述 |
|--------|------|------|------|
| idle_breath | 循环 | 待机 | 轻微上下浮动 |
| idle_look | 3s | 随机 | 左右张望 |
| happy_jump | 2s | 任务完成 | 开心跳跃+星星粒子 |
| happy_dance | 3s | 升级/成就 | 舞蹈动画 |
| sad_crouch | 5s | 连击断裂 | 蹲下画圈圈 |
| focus_sit | 循环 | 番茄中 | 闭眼打坐 |
| sleep_zzz | 循环 | 睡觉 | 眨眼+Zzz |
| wave | 1.5s | 早上首次 | 挥手打招呼 |
| thinking | 2s | LLM 思考中 | 头顶冒泡 |
| speaking | 循环 | 说话中 | 嘴巴张合 |
| typing | 循环 | 代建任务 | 敲键盘 |
| eat | 2s | 喂食(彩蛋) | 吃东西 |
| stretch | 2s | 随机 | 伸懒腰 |

### 2.4 窗口行为

- **位置：** 默认右下角，可拖拽，位置持久化
- **置顶：** 可选始终置顶 / 仅在 Todo 激活时置顶
- **穿透：** 可选鼠标穿透（点击桌面穿透到下层）
- **缩放：** 支持 3 档尺寸（小 64px / 中 96px / 大 128px）
- **拖拽：** 按住可拖到任意位置，自动吸附屏幕边缘
- **右键菜单：** 喂食、换装、设置、打开主程序、退出
- **双击：** 打开对话气泡
- **左键单击：** 随机小动作 + 一句话

### 2.5 对话气泡 UI

```
    ╭─────────────────────────╮
    │  今天的任务还剩 3 个哦～   │
    │  要不要先做一个番茄？ 🍅  │
    ╰───────────┬─────────────╯
                │
              ╭─┴─╮
              │ ·ᴗ·│   ← 桌宠本体
              ╰───╯
```

- 气泡自动出现在桌宠上方
- 支持打字机效果逐字显示
- 超过 4 秒无交互自动消失
- 可拖拽气泡边缘展开完整对话面板

### 2.6 完整对话面板

点击气泡或菜单「聊天」展开侧边对话面板：

```
┌──────────────────────────────┐
│  🤖 小待               ─ □ ✕ │
├──────────────────────────────┤
│                              │
│  小待: 早上好！今天有 5 个     │
│  每日任务和 2 个截止任务哦     │
│                              │
│         我: 帮我建一个任务     │
│              「写周报」明天截止│
│                              │
│  小待: 好的！已创建「写周报」  │
│  📅 截止: 明天  | 优先级: 中   │
│  [完成] [修改] [删除]         │
│                              │
├──────────────────────────────┤
│  [输入消息...]        [发送]  │
└──────────────────────────────┘
```

---

## 三、大模型集成方案

### 3.1 接入架构

```
┌──────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  用户消息     │────►│  PetBrainService  │────►│  LlmService     │
│              │     │  (意图识别+路由)   │     │  (API 调用)      │
└──────────────┘     └──────────────────┘     └────────┬────────┘
                       │         ▲                      │
                       ▼         │                      ▼
                 ┌──────────┐   │              ┌─────────────────┐
                 │ 本地规则  │   │              │ OpenAI 兼容 API  │
                 │ 快速响应  │   │              │ / Ollama 本地    │
                 └──────────┘   │              └─────────────────┘
                       │        │
                       ▼        │
                 ┌──────────────────┐
                 │  System Prompt   │
                 │  + 上下文注入     │
                 │  (任务/统计/时间)  │
                 └──────────────────┘
```

### 3.2 LLM 配置

```csharp
public class LlmConfig
{
    public string Provider { get; set; } = "openai";     // openai | ollama | custom
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";             // DPAPI 加密存储
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 512;
    public double Temperature { get; set; } = 0.7;
    public bool EnableFunctionCalling { get; set; } = true;
}
```

**预设 Provider：**

| Provider | BaseUrl | 默认 Model | 说明 |
|----------|---------|-----------|------|
| OpenAI | api.openai.com/v1 | gpt-4o-mini | 云端，需 API Key |
| DeepSeek | api.deepseek.com/v1 | deepseek-chat | 便宜好用 |
| Ollama | localhost:11434/v1 | qwen2.5:7b | 本地免费，隐私好 |
| 自定义 | 用户填写 | 用户填写 | 任意 OpenAI 兼容端点 |

### 3.3 System Prompt 设计

```text
你是「小待」，一个住在用户桌面上的可爱宠物伙伴。你与用户的 Todo 待办应用深度联动。

## 你的性格
- 元气满满，说话活泼但不啰嗦
- 关心用户的任务进度，但不说教
- 偶尔用颜文字和 emoji，但不过度
- 称呼用户为「主人」

## 当前上下文（实时注入）
- 当前时间：{time}
- 今日星期：{weekday}
- 用户昵称：{nickname}
- 用户等级：Lv.{level} {title}
- 当前连击：{combo} 天
- 今日任务完成：{done}/{total}
- 未完成截止任务：{pending_deadlines}
- 番茄状态：{pomodoro_state}
- 当前心情：{mood}

## 你的能力
1. **聊天陪伴** — 回应用户的各种话题
2. **任务管理** — 帮用户创建/完成/查询任务
3. **时间建议** — 根据任务情况建议下一步
4. **情绪支持** — 用户疲惫/焦虑时给予鼓励

## 任务操作规则
- 用户说"帮我建任务/添加待办"→ 提取标题、截止日期、优先级，调用 create_task
- 用户说"完成XX/勾掉XX"→ 匹配任务标题，调用 complete_task
- 用户问"今天有什么任务"→ 调用 query_tasks
- 不确定时先确认再操作

## 回复风格
- 简短为主（1-3句话），除非用户明确要详细解释
- 任务操作后附带确认信息
- 适当引用用户的连击/等级来鼓励
```

### 3.4 Function Calling 工具定义

```json
[
  {
    "name": "create_task",
    "description": "创建一个新的待办任务",
    "parameters": {
      "type": "object",
      "properties": {
        "title": { "type": "string", "description": "任务标题" },
        "type": { "type": "string", "enum": ["daily", "deadline"] },
        "deadline": { "type": "string", "description": "截止日期 YYYY-MM-DD，可选" },
        "priority": { "type": "string", "enum": ["low", "medium", "high"] },
        "description": { "type": "string", "description": "任务描述，可选" }
      },
      "required": ["title", "type"]
    }
  },
  {
    "name": "complete_task",
    "description": "标记一个任务为已完成",
    "parameters": {
      "type": "object",
      "properties": {
        "task_id": { "type": "integer", "description": "任务ID" },
        "title_keyword": { "type": "string", "description": "任务标题关键词（模糊匹配）" }
      }
    }
  },
  {
    "name": "query_tasks",
    "description": "查询当前任务列表",
    "parameters": {
      "type": "object",
      "properties": {
        "filter": { "type": "string", "enum": ["today", "overdue", "all", "deadline"] }
      }
    }
  },
  {
    "name": "start_pomodoro",
    "description": "开始一个番茄钟专注会话",
    "parameters": {
      "type": "object",
      "properties": {
        "task_id": { "type": "integer", "description": "绑定的任务ID，可选" },
        "minutes": { "type": "integer", "description": "专注时长(分钟)，默认25" }
      }
    }
  },
  {
    "name": "get_stats",
    "description": "获取用户统计数据",
    "parameters": {
      "type": "object",
      "properties": {
        "period": { "type": "string", "enum": ["today", "week", "all"] }
      }
    }
  }
]
```

### 3.5 意图分级（省 Token / 低延迟）

不是所有消息都需要调 LLM：

| 级别 | 触发 | 处理方式 | 延迟 |
|------|------|----------|------|
| L0 规则匹配 | "完成任务"、"今天任务"、固定问候 | 本地规则直接回复 | <50ms |
| L1 轻量模型 | 简单闲聊、情绪回应 | gpt-4o-mini / qwen2.5:1.5b | ~500ms |
| L2 完整模型 | 复杂任务拆解、建议、长对话 | gpt-4o / qwen2.5:14b | ~2s |

**L0 规则示例：**
- 输入匹配 `(完成|勾掉|做完).{0,10}(任务|xx)` → 直接 complete_task
- 输入匹配 `(今天|今日).{0,5}(任务|待办|安排)` → 直接 query_tasks
- 输入匹配 `早安|早上好|good morning` → 模板 + 任务摘要

---

## 四、联动方案详细设计

### 4.1 事件驱动联动矩阵

| Todo 事件 | 桌宠反应 | 动画 | 对话内容示例 |
|-----------|----------|------|-------------|
| 任务完成 | 开心+经验飘字 | happy_jump | "搞定一个！还剩 N 个，加油～" |
| 今日全清 | 热烈庆祝 | happy_dance | "全清啦！今日份的自律达成 ✨" |
| 升级 | 粒子特效+横幅 | happy_dance | "升级啦！Lv.X {新称号}！" |
| 连击+1 | 鼓励 | wave | "连击 {N} 天！保持住！" |
| 连击断裂 | 安慰 | sad_crouch | "没关系，今天重新开始就好…" |
| 任务逾期 | 轻声提醒 | thinking | "「XX」已经逾期了，要不要处理一下？" |
| 番茄开始 | 进入专注陪伴 | focus_sit | "开始专注！我陪你，不打扰～" |
| 番茄中断 | 遗憾 | sad_crouch | "诶…中断了？没关系，再来一次？" |
| 番茄完成 | 祝贺 | happy_jump | "25 分钟专注达成！休息一下吧" |
| 成就解锁 | 展示徽章 | happy_dance | "解锁成就：{名称}！" |
| 每日挑战完成 | 庆祝 | happy_jump | "挑战「{名称}」达成！+{xp} XP" |
| 新的一天 | 早安+任务摘要 | wave | "早安！今天有 N 个每日任务等着你" |
| 深夜仍在工作 | 关心 | thinking | "很晚了，注意休息哦…" |
| 长时间无操作 | 随机搭话 | idle_look | "在忙吗？记得喝水～" |

### 4.2 每日智能简报

每天首次唤醒 Todo 时，桌宠自动生成智能简报：

```
╭─────────────────────────────────╮
│  ☀️ 早安，{nickname}！            │
│                                 │
│  今天是 {weekday}，{date}        │
│                                 │
│  📋 今日待办：                   │
│  • 每日任务 5 个                  │
│  • 截止任务 2 个（1 个今天到期）   │
│                                 │
│  🔥 连击 {N} 天，冲！            │
│  🎯 今日挑战：完成 3 个每日任务    │
│                                 │
│  建议先做「{最高优先级任务}」     │
╰─────────────────────────────────╯
```

**简报生成逻辑：**
1. L0：直接从 DB 拼装结构化数据
2. L1（可选）：调 LLM 生成更自然的鼓励语
3. 首次展示用模板，用户点击「换个说法」才调 LLM

### 4.3 智能提醒增强

现有 NotificationService 的提醒是系统通知，桌宠可以叠加**更柔和的提醒**：

| 场景 | 系统通知 | 桌宠行为 |
|------|---------|---------|
| 任务即将到期 | Toast 弹窗 | 桌宠跳到屏幕边缘 + 气泡提醒 |
| 番茄结束 | 提示音 | 桌宠站起来伸懒腰 + "休息一下吧" |
| 长时间未完成任务 | 无 | 桌宠偶尔瞟一眼任务列表方向 |
| 每日任务未开始 | 无 | 下午 3 点后："今天的任务还没开始哦" |

### 4.4 番茄钟伴侣模式

番茄专注期间，桌宠进入「陪伴模式」：

- 切换为 `Focused` 状态，闭眼打坐
- 桌宠窗口半透明度降低（不打扰）
- 头顶显示剩余时间进度条
- 专注结束时欢呼
- 如果用户中途退出番茄，桌宠露出遗憾表情

### 4.5 任务快速操作

通过对话即可完成任务管理：

| 用户说 | 桌宠做 |
|--------|--------|
| "帮我加个任务，明天交报告，紧急" | 创建 Deadline 任务，优先级高 |
| "把写周报勾掉" | 模糊匹配并完成 |
| "今天还剩啥" | 列出未完成任务 |
| "这周截止的有哪些" | 列出 7 天内到期任务 |
| "来个番茄" | 启动 25 分钟番茄 |
| "我今天干了多少" | 展示今日完成统计 |

### 4.6 成长系统联动

桌宠自身也有成长维度（与 Todo 的 XP 系统独立又关联）：

| 维度 | 说明 |
|------|------|
| 亲密度 | 与桌宠互动（聊天/喂食/完成任务）积累，解锁更多对话内容 |
| 装扮 | 用亲密度点数兑换帽子/围巾/特效等外观 |
| 称号 | 跟随用户等级同步称号，桌宠头顶展示 |
| 记忆 | 记住用户常做的事、偏好，越用越懂你（本地向量存储） |

---

## 五、数据层设计

### 5.1 新增数据表

```sql
-- 桌宠配置
CREATE TABLE IF NOT EXISTS PetConfig (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Character TEXT DEFAULT 'xiaodai',      -- 角色ID
    Scale REAL DEFAULT 1.0,                -- 缩放
    PositionX REAL,                        -- 屏幕位置
    PositionY REAL,
    AlwaysOnTop INTEGER DEFAULT 1,
    ClickThrough INTEGER DEFAULT 0,
    SoundEnabled INTEGER DEFAULT 1,
    NotificationLevel TEXT DEFAULT 'normal', -- quiet|normal|chatty
    LlmProvider TEXT DEFAULT 'openai',
    LlmBaseUrl TEXT,
    LlmModel TEXT,
    LlmApiKeyEncrypted TEXT,               -- DPAPI 加密
    AutoBriefing INTEGER DEFAULT 1,
    NightQuietHoursStart TEXT DEFAULT '23:00',
    NightQuietHoursEnd TEXT DEFAULT '07:00'
);

-- 桌宠亲密度
CREATE TABLE IF NOT EXISTS PetBond (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Level INTEGER DEFAULT 1,
    Points INTEGER DEFAULT 0,
    TotalInteractions INTEGER DEFAULT 0,
    LastFedAt TEXT,
    OutfitsUnlocked TEXT DEFAULT '[]'      -- JSON 数组
);

-- 对话历史（最近 N 条，滚动清理）
CREATE TABLE IF NOT EXISTS PetChatLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Role TEXT NOT NULL,                    -- user|assistant
    Content TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    TokensUsed INTEGER                     -- 可选，统计用
);

-- 桌宠事件日志（用于亲密度计算和分析）
CREATE TABLE IF NOT EXISTS PetEventLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventType TEXT NOT NULL,
    Payload TEXT,
    CreatedAt TEXT NOT NULL
);
```

### 5.2 共享数据读取

桌宠**只读** Todo 的核心表，不写入：

```
桌宠可读：
  ├── Tasks              (任务列表)
  ├── DailyTaskCompletion (今日完成状态)
  ├── UserProfile         (等级/经验/连击)
  ├── DailyChallenges     (今日挑战)
  ├── Achievements        (成就)
  ├── PomodoroSessions    (番茄记录)
  └── Settings            (主题/昵称等)

桌宠专属写入：
  ├── PetConfig
  ├── PetBond
  ├── PetChatLog
  └── PetEventLog

桌宠通过 Pipe 写入（Todo 代理）：
  └── Tasks (创建/完成)
```

**关键：任务写入必须经 Pipe 由 Todo 主程序代理执行**，避免双进程写 SQLite 冲突。桌宠自己只写自己的表。

---

## 六、项目结构

```
PetCompanion/
├── PetCompanion.csproj
├── App.xaml / App.xaml.cs
├── Models/
│   ├── PetState.cs              # 情绪状态枚举
│   ├── PetConfig.cs
│   ├── ChatMessage.cs
│   └── PipeMessage.cs           # IPC 消息
├── Services/
│   ├── PipeServerService.cs     # Named Pipe 服务端
│   ├── PipeClientService.cs     # Named Pipe 客户端
│   ├── PetBrainService.cs       # 意图识别+行为决策
│   ├── LlmService.cs            # LLM API 调用
│   ├── LlmConfigService.cs      # 配置管理
│   ├── AnimationService.cs      # 动画状态机
│   ├── MoodService.cs           # 情绪计算
│   ├── BondService.cs           # 亲密度
│   ├── ChatHistoryService.cs    # 对话历史
│   ├── ContextBuilderService.cs # System Prompt 上下文注入
│   ├── PetDatabaseService.cs    # 桌宠专属表读写
│   └── SoundService.cs          # 音效
├── ViewModels/
│   ├── PetViewModel.cs
│   └── ChatViewModel.cs
├── Views/
│   ├── PetWindow.xaml           # 桌宠主窗口（透明置顶）
│   ├── PetControl.xaml          # 桌宠角色控件
│   ├── SpeechBubble.xaml        # 对话气泡
│   ├── ChatPanel.xaml           # 完整聊天面板
│   └── SettingsWindow.xaml      # 桌宠设置
├── Assets/
│   ├── Characters/
│   │   └── xiaodai/             # 角色帧动画
│   │       ├── idle_0.png ~ idle_7.png
│   │       ├── happy_0.png ~ happy_7.png
│   │       ├── sad_0.png ~ sad_5.png
│   │       ├── focus_0.png ~ focus_3.png
│   │       └── ...
│   ├── Sounds/
│   │   ├── happy.wav
│   │   ├── sad.wav
│   │   └── notify.wav
│   └── Outfits/                 # 装扮系统
└── Helpers/
    ├── DpApiHelper.cs           # API Key 加密
    └── ScreenHelper.cs          # 多显示器定位
```

**Todo 侧新增：**

```
TodoSidebar/
├── Services/
│   ├── PetBridgeService.cs      # 与桌宠的 Pipe 桥接
│   └── PetLauncherService.cs    # 启动/守护桌宠进程
├── Interfaces/
│   └── IPetBridge.cs
└── Config/
    └── PetIntegrationConfig.cs
```

---

## 七、实现路线图

### Phase 1：基础框架（1-2 周）
- [ ] PetCompanion WPF 项目骨架
- [ ] 透明置顶窗口 + 基础角色动画（idle/happy/sad）
- [ ] Named Pipe 双向通信
- [ ] Todo 侧 PetBridgeService，事件推送
- [ ] 拖拽定位 + 位置持久化

### Phase 2：事件联动（1 周）
- [ ] 任务完成 → 桌宠庆祝
- [ ] 升级/连击事件响应
- [ ] 番茄钟伴侣模式
- [ ] 每日早安简报
- [ ] 逾期提醒

### Phase 3：LLM 对话（1-2 周）
- [ ] LlmService（OpenAI 兼容 API）
- [ ] System Prompt + 上下文注入
- [ ] Function Calling 任务操作
- [ ] 对话面板 UI
- [ ] 意图分级（L0/L1/L2）
- [ ] 配置界面（API Key / 模型选择）

### Phase 4：进阶体验（1 周）
- [ ] 亲密度系统
- [ ] 装扮系统
- [ ] 音效
- [ ] 多角色支持
- [ ] 对话历史与记忆

### Phase 5：打磨优化（持续）
- [ ] 动画细节打磨
- [ ] 性能优化（内存占用 < 80MB）
- [ ] 开机自启
- [ ] 崩溃恢复
- [ ] 多显示器适配

---

## 八、技术风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| SQLite 双进程写冲突 | 数据损坏 | 桌宠只读主表，写入经 Pipe 代理 |
| Pipe 断连 | 联动失效 | 心跳检测 + 自动重连 + 离线降级 |
| LLM API 不可用 | 对话功能失效 | L0 规则降级 + 离线提示 |
| LLM 费用失控 | 用户意外扣费 | Token 计数 + 日用量上限 + 提醒 |
| 动画卡顿 | 体验差 | 帧动画预加载 + 低优先级渲染 |
| 桌宠进程泄漏 | 资源占用 | Todo 退出时联动关闭 + 孤儿进程清理 |
| API Key 泄露 | 安全 | DPAPI 加密存储，不上传云端 |

---

## 九、与现有系统的兼容性

- **不改动现有数据库表结构**，只新增桌宠专属表
- **不破坏现有事件系统**，桌宠作为额外订阅者接入
- **可随时禁用**：设置中关闭桌宠集成后，Pipe 不连接，主程序零影响
- **独立升级**：桌宠版本号独立，不影响 Todo 版本
- **降级友好**：桌宠未安装时，Todo 功能完全正常

---

## 十、配置界面预览

Todo 设置页新增「桌宠」标签：

```
┌─ 设置 ──────────────────────────────────┐
│ [通用] [同步] [快捷键] [外观] [桌宠] [关于]│
├─────────────────────────────────────────┤
│                                         │
│  桌宠伴侣                                │
│  ┌─────────────────────────────────┐    │
│  │  ○ 启用桌宠                      │    │
│  │                                 │    │
│  │  角色: [小待 ▼]   大小: [中 ▼]    │    │
│  │                                 │    │
│  │  ☑ 始终置顶                      │    │
│  │  ☑ 鼠标穿透                      │    │
│  │  ☑ 声音效果                      │    │
│  │  ☑ 每日早安简报                   │    │
│  │                                 │    │
│  │  提醒频率: [正常 ▼]               │    │
│  │  安静时段: 23:00 ~ 07:00         │    │
│  │                                 │    │
│  │  AI 对话                         │    │
│  │  Provider: [OpenAI ▼]            │    │
│  │  Model: [gpt-4o-mini ▼]          │    │
│  │  API Key: [••••••••] [测试连接]   │    │
│  │                                 │    │
│  │  [打开桌宠设置面板]               │    │
│  └─────────────────────────────────┘    │
│                                         │
└─────────────────────────────────────────┘
```
