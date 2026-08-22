# TodoSidebar UI 迭代级升级方案 V2 —— 「个人效率工作台」

> 基于第一轮（设计令牌 + 控件现代化）之上的**结构性重构**。
> 示意图见 `docs/mockups/`，用浏览器打开即可查看实际效果：
> - `mockup-01-sidebar-light.svg` 侧边栏 · 浅色
> - `mockup-02-sidebar-dark.svg` 侧边栏 · 深色
> - `mockup-03-full-dashboard.svg` 完整模式 · 今日仪表盘（导航栏布局）
> - `mockup-04-focus.svg` 沉浸式专注页
> - `mockup-05-stats.svg` 统计仪表盘 + 成就墙

---

## 一、本轮反馈的两个问题

1. **图标缺失**：第一轮采用 Segoe Fluent Icons / MDL2 字体字形，部分码位在目标机器字体中无覆盖 → 渲染空白。**V2 方案改为自绘矢量路径图标库（Path Geometry），零字体依赖，任何机器像素一致**，作为第一个工作包优先落地。
2. **改动幅度不够**：第一轮是"换皮"（令牌 + 样式），V2 是**信息架构 + 布局结构 + 交互模式的重构**。

## 二、定位转变

| | 现在（v4.2.2） | V2 目标 |
|---|---|---|
| 产品形态 | 任务列表工具窗口 | 个人效率工作台 |
| 侧边栏 | 单一任务列表 + 零散小部件 | **今日驾驶舱**：问候、进度环、分组任务流、本周概览、迷你番茄 Dock |
| 完整模式 | 顶部 Tab 切换的五个页面 | **左侧导航栏（Navigation Rail）+ 页面内容区**，可扩展任意数量页面 |
| 统计 | 数字罗列 | **数据仪表盘**：KPI 网格 + 趋势图 + 成就墙 |
| 专注 | 表单式番茄页 | **沉浸式全屏场景**：氛围渐变、超大环、回合点、统计条 |
| 操作方式 | 逐个点击 | **命令面板 Ctrl+K** 全局搜索/跳转/动作 |

## 三、五大界面重设计（对照示意图）

### 1. 侧边栏「今日驾驶舱」（mockup-01/02）
- **问候头部**：渐变头像 + "早上好，Alex" + 日期与待办数；右侧设置/收起按钮
- **Hero 进度卡**：完成率环 68% + 13/19 大数字 + 连击 chip + 等级胶囊（合并现在分散的三块信息）
- **Composer 快速添加**：占位提示 + 回车提交 + 渐变添加钮（替代输入框+日期选择器两行）
- **任务流**：圆形勾选钮、类型/紧急度软底 chips、子任务进度条、已完成态置灰划线并显示完成时间；"已完成 · N" 默认折叠
- **本周概览条**：七日格子 + 完成绿点，一眼看到节奏
- **底部 Dock**：迷你番茄环 + 时间 + 播放键，右侧统计/同步/设置三枚图标（替代文字按钮）

### 2. 完整模式「今日仪表盘」（mockup-03）
- 窗口默认宽度 450 → **560**（最小 520），容纳双列
- **Navigation Rail**：Logo + 六个入口（今日/截止/历史/统计/专注/成就）+ 底部同步状态灯与设置；激活项带左侧指示条与浅底胶囊——为未来扩展页面留好骨架
- **顶栏**：页面标题 + 日期、全局搜索胶囊（Ctrl K）、主题切换、头像
- **KPI 行**：今日进度环卡 / 连击卡（近 7 日点阵）/ 等级经验卡
- **双列内容**：左列任务流（今日 + 截止分组，逾期红轨）；右列今日挑战（进度条化）、快速专注卡、同步状态卡
- 底部快捷键提示行

### 3. 沉浸式专注页（mockup-04）
- 深色氛围底 + 中心环境光晕（跟随品牌色呼吸动画）
- **超大渐变进度环**（120px 半径），时间数字居中，状态副文案
- 回合圆点 ×4、绑定任务胶囊 chip（可点击切换）
- 主操作：大号渐变"暂停"胶囊 + 描边"停止"；空格开始/暂停、Esc 停止
- 底部统计条：今日番茄 / 专注时长 / 本日经验 / 连击 四格

### 4. 统计仪表盘（mockup-05）
- KPI 2×2 网格：累计完成(+12% 同比)、平均完成率环、连击天数、高优待办（带"立即处理"动作）
- **近 14 天柱状图**：今天高亮渐变柱，其余浅靛；日均徽章
- **经验成长折线图**：30 天趋势 + 面积填充 + 升级差值提示（Lv.12→13 还差 180 EXP）
- **成就墙预览**：已解锁彩色圆徽 + 未解锁灰锁态 + 稀有描边；点击进独立成就页

### 5. 成就页（新增页面）
- 徽章网格全览：稀有度边框（普通/稀有/史诗）、解锁进度环、解锁时间
- 复用 Rail 导航，无需新窗口

## 四、设计令牌 V2

1. **完整色阶**：品牌色生成 50–900 十一阶（Indigo），暗色自动取高一档；新增 `Accent.Soft/Hover/Pressed/Glow` 已有基础上补齐 `Accent.100~600`
2. **多强调色换肤**（设置页色板）：Indigo 靛 / Ocean 海蓝 / Sunset 落日 / Forest 松林 / Mono 黑白 —— 仅替换令牌字典即全局生效
3. **阴影体系**：`Elevation.0/1/2/3` 四级（卡片/悬浮/弹层/模态），统一替代散落的 DropShadowEffect 参数
4. **动效规范**：曲线 `CubicOut(200ms)` 入场、`CubicInOut(240ms)` 移动、`Spring(320ms)` 弹性确认；页面转场 = 内容淡入 + 12px 上移；卡片增删 FLIP 重排动画
5. **字阶**：Display 28 / Title 20 / Heading 15 / Body 13 / Caption 11 / Micro 9.5，全部令牌化

## 五、矢量图标库 AppIcon v2（修复缺字）

- `Controls/Icons/` 下新建 `IconPaths.cs`：每个图标一条 `Geometry` 数据（24×24 网格手绘），首期 24 枚：
  search / settings(slider) / logout / chevron×4 / check / delete / add / close / upload / download / play / pause / stop / calendar / clock / checklist / chart / star / eye / lock / info / save / sync / filter
- `AppIcon` 控件改为渲染 Path（保留 Glyph 模式作后备）；所有引用处按名称调用：`<controls:AppIcon Name="Search"/>`
- 特性：随 Foreground 着色、随 FontSize 缩放、命中区域不依赖文本度量、深浅主题天然一致

## 六、工程结构调整

```
TodoSidebar/
├── Themes/                  # 既有令牌体系（V2 扩充色阶/阴影/动效）
├── Controls/
│   ├── Icons/               # AppIcon v2 + IconPaths
│   ├── RingProgress.cs      # 环形进度（支持中心内容）
│   ├── BarChart.cs          # 轻量柱状图
│   ├── LineChart.cs         # 折线图（面积填充）
│   └── NavigationRail.cs    # 导航栏控件
├── Views/                   # 页面级 UserControl（替代 FullWindow 内联面板）
│   ├── TodayPage.xaml       # 今日仪表盘
│   ├── DeadlinesPage.xaml   # 截止（按逾期/今天/本周分组）
│   ├── HistoryPage.xaml
│   ├── StatsPage.xaml
│   ├── FocusPage.xaml
│   └── AchievementsPage.xaml
└── Controls/CommandPalette.xaml  # Ctrl+K 命令面板
```

- FullWindow 从 700 行内联 XAML 变为 **壳（Rail + 内容寄主）+ 页面**；MainWindow 保持轻量驾驶舱
- ViewModel 不动数据层，仅新增页面级投影属性（分组视图、图表序列）

## 七、实施阶段（预计 15–18 人日）

| 阶段 | 内容 | 工作量 |
|---|---|---|
| W1 | AppIcon v2 矢量图标库，全量替换（修缺字） | 1.5 天 |
| W2 | 令牌 V2：色阶/阴影/动效/多强调色字典 + 设置页色板 | 1.5 天 |
| W3 | NavigationRail + FullWindow 壳改造 + 页面路由 | 2 天 |
| W4 | TodayPage 仪表盘（KPI 卡 + 双列 + 挑战卡） | 2.5 天 |
| W5 | 任务卡组件化 + 分组时间轴（截止按紧急度分组） | 2 天 |
| W6 | FocusPage 沉浸式 + StatsPage 图表（RingProgress/BarChart/LineChart） | 3 天 |
| W7 | 成就页 + CommandPalette(Ctrl+K) | 2 天 |
| W8 | 动效打磨（FLIP/转场/骨架屏）+ 双主题走查 + DPI 验收 | 1.5 天 |

每阶段独立可交付、编译通过后人工验收，随时可停在任意阶段。

## 八、风险与对策

| 风险 | 对策 |
|---|---|
| 自绘图标准确度 | 以 24px 网格统一绘制 + 视觉评审对照 Material Symbols 形状语言 |
| 页面化改造回归 | 旧 Tab 逻辑保留为兼容分支，逐页切换后移除；现有测试不动数据层 |
| 窗口加宽影响习惯 | 提供"紧凑宽度(450)"设置开关，Rail 可折叠为纯图标 |
| 图表性能 | 数据点 ≤ 60，Path 冻结 + BitmapCache，无第三方图表库 |
