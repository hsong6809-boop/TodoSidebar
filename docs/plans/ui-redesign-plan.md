# TodoSidebar 前端 UI 现代化改造方案

> 适用版本：v4.2.2 · WPF (.NET 8) · 目标：在不动业务逻辑的前提下，将界面升级为一套统一、现代、双主题完备的设计系统。
> 设计基调建议：**Fluent 2 / WinUI 3 气质 + 待办工具的克制感**（参照 Things 3 / TickTick 的信息密度与 Fluent 的材质语言），沿用现有 Indigo `#6366F1` 作为品牌主色，不做颠覆式改版。

---

## 一、现状诊断

### 1.1 总体评价

当前 UI 已有不错的骨架：`DynamicResource` 换肤、圆角卡片、Tailwind 色板、AnimationService 动效基础。主要差距在于**系统性不足**——没有设计令牌层，样式靠各窗口自行拼凑，导致视觉不一致、暗色主题残缺、原生控件穿帮。

### 1.2 问题清单（按严重程度）

| 级别 | 问题 | 证据 |
|---|---|---|
| P0 | **硬编码颜色散落 56 处**，绕过主题系统 | `MainWindow.xaml:163/485/701`、`FullWindow.xaml:46/73/190`、`LoginWindow.xaml` 全文 |
| P0 | **悬停态用黑色半透明遮罩**（`#10000000~#20000000`），暗色主题下几乎不可见 | `App.xaml:197/246/380`、`MainWindow.xaml:37`、`FullWindow.xaml:46/73` |
| P0 | **原生控件未模板化**：CheckBox、RadioButton、ComboBox、DatePicker、ProgressBar（默认绿色渐变！）、ScrollBar、ToolTip 在暗色下穿帮；XP 进度条是 WTP 默认绿 | `FullWindow.xaml:211/581/680`、`MainWindow.xaml:184/238` |
| P1 | **Emoji 充当功能图标**（🔍⚙🚪⤢◀✓×📌📋▶⏸⏹⬆️⬇️📊），彩色表情与极简风冲突、无法继承前景色、跨系统渲染不一 | `MainWindow.xaml:90-137/631-653`、底部栏等 |
| P1 | **登录页完全脱离主题**：写死白色系 + 旧品牌色 `#5B5FE9`（现主色已是 `#6366F1`），品牌断裂 | `LoginWindow.xaml:15/45/79-236` |
| P1 | **假毛玻璃**：`GlassBrush` 只是半透明纯色，无任何模糊；README 宣称的 `Helpers/BlurHelper.cs` 并不存在 | `App.xaml:33`、README 项目结构节 |
| P1 | **两个窗口全 C# 手搓 UI**（含 FrameworkElementFactory 复制按钮模板），无法复用共享样式 | `SettingsWindow.cs`(348行)、`StatisticsWindow.cs`(422行) |
| P2 | **令牌混乱**：圆角 4/6/8/10/12/14/16 七种混用；字号 9~34 十三档无字阶；间距随手定 | 全部 XAML |
| P2 | **交互密度问题**：任务卡上 ✓/× 常驻造成视觉噪音；无空状态占位；搜索结果切换无过渡；命中区域仅 24~28px | `MainWindow.xaml:520-539` |
| P2 | **统计页只是数字罗列**，无图表；成长曲线藏在独立统计窗里 | `FullWindow.xaml:411-530` |
| P2 | 无 Win11 集成：无 DWM 圆角、无 Mica/亚克力背板 | 全局 |
| P3 | 样式重复定义：`IconButton`(App) ≈ `SidebarButton`(MainWindow) ≈ `HeaderIconButton`/`ToolbarTextButton`(FullWindow) | 三处各自维护 |

---

## 二、目标设计语言

### 2.1 设计原则

1. **一个强调色**：Indigo 只用于可交互元素与关键进度；成功/警告/危险色只用于状态，不参与装饰。
2. **中性色分层靠亮度而非透明黑**：所有 hover/pressed 用主题感知的 tint 色，禁止 `#xx000000`。
3. **圆角三级制**：S=8（输入框/小按钮）、M=12（卡片/弹窗）、L=16（窗口主体）、胶囊=999。
4. **图标一律矢量**（Segoe MDL2 Assets / Fluent Icons），游戏化装饰位（🍅🏅🎯）可保留 emoji 作为有意的品牌个性。
5. **动效克制**：120/200/320ms 三档时长 + CubicEase Out，只服务反馈与层级，不炫技。

### 2.2 设计令牌（Design Tokens）

新增语义化资源字典，亮/暗各一套，键名不变值随主题：

| 类别 | Token（示例值 亮/暗） |
|---|---|
| 表面 | `Surface.Base` #F7F8FC(92%) / #0F172A(94%)；`Surface.Card` #FFFFFF(96%) / #1E293B；`Surface.Hover` #0F172A@6% / #FFFFFF@8%；`Surface.Pressed` @10%/12% |
| 文本 | `Text.Primary` #0F172A / #F1F5F9；`Text.Secondary` #64748B / #94A3B8；`Text.Tertiary` #94A3B8 / #64748B；`Text.OnAccent` #FFFFFF |
| 描边 | `Border.Subtle` #E2E8F0 / #FFFFFF@10%；`Border.Strong` #CBD5E1 / #FFFFFF@18% |
| 强调 | `Accent` #6366F1 / #818CF8；`Accent.Hover` #4F46E5 / #A5B4FC；`Accent.Pressed` #4338CA / #C7D2FE；`Accent.Soft` #6366F1@12% / #818CF8@16% |
| 状态 | Success/Warning/Danger 三色 + 各自 `.Soft` 底（12% tint），亮暗各调一档明度 |
| 圆角 | `Radius.S`=8、`Radius.M`=12、`Radius.L`=16（WPF 用 CornerRadius 资源） |
| 间距 | 4 / 8 / 12 / 16 / 20 / 24 |
| 字阶 | Caption 11 / Body 13 / Subtitle 15 / Title 17 / Display 28；计时数字启用等宽数字特性 |
| 阴影 | `Shadow.Card`(0,2,8,@8%)、`Shadow.Flyout`(0,8,24,@14%) —— 仅用于浮层，列表项禁用 Effect |
| 动效 | `Duration.Fast`=120ms、`Duration.Base`=200ms、`Duration.Slow`=320ms |

---

## 三、分阶段实施方案

### P0 地基：设计令牌与主题引擎（约 1~2 天）

1. **拆分 App.xaml** → `Themes/Tokens.Light.xaml`、`Themes/Tokens.Dark.xaml`、`Themes/Styles.Controls.xaml`（控件样式）、`Themes/Styles.Templates.xaml`（DataTemplate），App.xaml 只留 MergedDictionaries 与转换器注册。
2. **重写 ThemeManager**：由逐个 set brush 改为整本替换令牌字典（保留 `CurrentTheme`/`ThemeChanged` 公共 API 不变，调用方零改动）。
3. **清零硬编码颜色**（对照 1.2 表逐一处理）：
   - 两处 `#FF8A00` 连击文字 → 新增 `Combo.Brush` 令牌；
   - `TriggerStrip` 的 `#6366F1` → `{DynamicResource Accent}`；
   - 焦点光晕 `DropShadowEffect Color=#5B5FE9` → 取 `Accent`；
   - 全部黑色悬停遮罩 → `Surface.Hover`。
4. 统一现有圆角/字号到令牌档位（机械替换，不改布局）。

### P1 基础控件现代化（约 2~3 天）

1. **矢量图标体系**：新建 `Controls/AppIcon`（TextBlock 子类，FontFamily = Segoe Fluent Icons，fallback Segoe MDL2 Assets）+ `Icons.g.cs` 常量表。核心映射：搜索 `\uE721`、设置 `\uE713`、登出 `\uF3B1`、收起 `\uE76B`、展开 `\uE8A7`、完成 `\uE73E`、删除 `\uE74D`、加号 `\uE710`、上传 `\uE898`、下载 `\uE8AB`、日历 `\uE787`、播放/暂停/停止 `\uE768/\uE769/\uE71A`、图表 `\uE9D2`、星标 `\uE735`。替换 MainWindow/FullWindow/对话框中全部功能性 emoji。
2. **按钮家族**：Primary（实心）/ Secondary（描边）/ Ghost（透明，hover 显示 Surface.Hover）/ Danger-Ghost 四款 × 大中小尺寸；按压统一缩放 0.98 + `Accent.Soft` 悬停；补键盘焦点可视环（2px Accent 外圈）。
3. **输入框**：GlassTextBox 增加 Placeholder 附加属性（浮动提示可选）；聚焦环改为 1.5px Accent 描边 + Accent.Soft 内晕，替代现在的 DropShadowEffect。
4. **CheckBox/RadioButton 自绘模板**：任务完成用圆形勾选框（空圈→Accent 填充→白色对勾），优先级选择器改为 pill 形分段选择。
5. **ProgressBar → XP 胶囊条**：基于现有 `CircularProgress` 思路新写 `CapsuleProgress` 控件（圆角轨道 + 渐变填充 + 可选流光动画），替换两处等级条。
6. **ComboBox / DatePicker / ToolTip 暗色适配模板**：下拉弹层用 `Surface.Card`+`Shadow.Flyout`。
7. **细滚动条**：6px 圆角拇指，悬停加深，空闲自动淡化。

### P2 窗口外壳与登录页（约 2~3 天）

1. **`Services/DwmBackdropHelper.cs`（新建）**：
   - Win11 22H2+：`DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` 启用 Mica/Acrylic，窗口背景改低不透明度令牌，实现真·毛玻璃；
   - Win11 初版：DWM 圆角偏好（`DWMWCA_WINDOW_CORNER_PREFERENCE=ROUND`）；
   - Win10 及失败回退：维持现有半透明纯色（`AllowsTransparency` 路径保留）。
   - 按 OS 版本与调用结果静默降级，不弹错。
2. **统一窗口壳**：抽取通用标题栏样式（32px 高拖拽区、右侧 36×32 图标按钮位、统一关闭钮），MainWindow / FullWindow / 四个对话框共用。
3. **LoginWindow 重做**：接入主题令牌；左侧或顶部加品牌渐变块（Indigo→Violet 对角渐变 + 应用 logo）；邮箱/密码输入框加前置图标与"显示密码"眼睛开关；登录按钮增加加载态（转圈 + 禁用）；错误提示改行内 Alert 条。
4. **SettingsWindow / StatisticsWindow 迁移为 XAML**：UI 结构原样翻译成 XAML 并复用共享样式，删除 FrameworkElementFactory 手搓模板；事件处理逻辑保持不变（纯换皮，回归风险可控）。

### P3 核心界面重设计（约 3~5 天）

1. **任务卡重做**（侧边栏 + 完整模式共用一套模板）：
   - 左端放圆形勾选框，**单击即完成**（替代常驻 ✓ 按钮）；删除收进悬停显现的 Ghost 图标或右键菜单；
   - 元信息改 chip 化：截止倒计时 pill（逾期红/当天橙/远期灰）、子任务迷你进度条（细胶囊）、类型色 3px 左轨保留但圆角化；
   - 悬停整卡 `Surface.Hover` + 轻微上浮 1px（复用现有 TranslateTransform）。
2. **快速添加 Composer**（侧边栏）：单输入框 + 类型分段切换（每日/截止）+ 日期 chip；Enter 提交，提交后清空并播放入列动画。
3. **分组与空状态**：列表头"待完成 N"可折叠；"今日已完成"默认折叠成计数徽章；空列表显示居中插画（简单 Path 图形）+ 一句引导文案 + 快捷键提示。
4. **完整模式 Tab → 分段控件**：胶囊容器 + 滑动指示器（指示器 TranslateTransform.X 动画 200ms）；Tab 内容切换加 8px 位移交叉淡入。
5. **截止页分组**：按 已逾期 / 今天 / 未来 7 天 / 更远 分组渲染，组头带计数与对应状态色。
6. **统计页可视化**：KPI 双列卡网格（数值 Display 字阶）；新增轻量柱状图控件 `BarChart`（ItemsControl+Rectangle 即可，绑定近 7 天完成数）；完成率用现有 `CircularProgress` 画环。独立 StatisticsWindow 内容并入该页后，窗口版本可退役。
7. **专注页**：大环改渐变描边（Accent→Success），时间数字等宽防抖动；下方加 4 个回合圆点表示番茄轮次；开始按钮升为 Primary 大按钮。
8. **对话框统一**：TaskDetailDialog / UpgradeDialog（权益卡改成两张对比卡）/ AchievementWindow（徽章网格：未解锁灰度+锁角标，稀有度描边色）。

### P4 动效统一与打磨（约 1~2 天)

1. AnimationService 时长/缓动接入令牌；列表载入加 30ms 交错延迟。
2. 勾选框打勾描边动画（StrokeDashOffset）；XP 条数值变化时平滑填充。
3. 升级横幅重做：渐变描边 + 外发光 + 弹性入场（现有粒子保留）。
4. 合并三份重复的图标按钮样式为一份。
5. 收尾检查：两主题对比度（正文 ≥4.5:1）、DPI 125%/150%、`Settings` 里加"减少动态效果"开关尊重系统动画设置。

---

## 四、关键技术要点

- **主题切换**：令牌字典整体替换时，`DynamicResource` 引用会自动刷新；注意 `StaticResource PriorityHighBrush` 这类写法（FullWindow 底部优先级选择器在用）必须改成 DynamicResource，否则换肤失效。
- **AllowsTransparency 与 DWM 背板互斥**：启用系统背板的窗口不能开 `AllowsTransparency`，需在 DwmHelper 里按能力分支调整窗口属性，务必保留现有纯透明路径作为回退。
- **性能红线**：ListBoxItem 与常驻元素禁用 BitmapEffect/DropShadowEffect（现有 GlassTextBox 焦点光晕保留即可）；阴影用预合成 PNG 或仅限浮层。
- **图标兼容**：Segoe Fluent Icons 仅 Win11；基线字体设 Segoe MDL2 Assets（Win10 1809+ 系统自带），检测到 Fluent 再升级，glyph 编码两者基本同源。

## 五、工作量与顺序

| 阶段 | 内容 | 预估 | 依赖 |
|---|---|---|---|
| P0 | 令牌字典 + ThemeManager 重写 + 硬编码清零 | 1~2 天 | 无 |
| P1 | 控件库现代化（图标/按钮/输入/选择/进度/滚动条） | 2~3 天 | P0 |
| P2 | 窗口壳/DWM 背板/登录页/两个 C# 窗口迁 XAML | 2~3 天 | P0（部分依赖 P1） |
| P3 | 任务卡/Composer/分段 Tab/统计可视化/对话框 | 3~5 天 | P0、P1 |
| P4 | 动效统一、可访问性、验收 | 1~2 天 | 全部 |

合计约 9~15 个人日。若要先见效，**一日快赢包**：P0 全部 + 替换顶栏五个 emoji 图标 + XP 胶囊条 + 细滚动条 + 空状态文案，即可解决最刺眼的穿帮与噪音。

## 六、验收标准

1. `grep -r "#[0-9A-Fa-f]{6}" *.xaml *.cs` 中除令牌字典外零硬编码颜色；
2. 亮/暗两主题逐屏走查：所有交互元素具备 hover/pressed/focus/disabled 四态且无穿帮原生控件；
3. 正文对比度两主题均 ≥ 4.5:1；
4. 100%/125%/150% DPI 下无模糊错位；Win10 1809+ 与 Win11 均正常（背板优雅降级）；
5. 现有单元测试全绿，命令绑定与业务逻辑零改动（纯呈现层 diff）。

## 七、风险与对策

| 风险 | 对策 |
|---|---|
| Mica/亚克力在不同显卡/远程桌面下失效 | 能力探测 + 静默回退半透明方案，永不阻塞启动 |
| C# 窗口迁 XAML 引入回归 | 逐控件对照翻译，先迁 SettingsWindow 验证流程再迁 StatisticsWindow |
| 图标字体缺字形（特殊符号） | 上线前用字形覆盖表核对；缺失处回退 emoji |
| 动画过多影响低端机 | 全局 Duration 令牌一处可关；列表项不用像素着色器效果 |
