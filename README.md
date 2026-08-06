# 每日任务 (TodoSidebar)

一款现代化的 Windows 桌面待办管理应用，支持**侧边栏**和**完整窗口**双模式，带有 Supabase 云同步功能。

## ✨ 功能特性

### 🎨 双模式界面
- **侧边栏模式**：贴边显示，不占用工作区域，鼠标悬停自动展开/收起
- **完整模式**：全功能界面，支持标签页切换

### 📋 任务管理
- ✅ **每日任务**：建立一次，每天自动刷新，今天完成的明天重新出现
- 📅 **截止任务**：带有截止日期的项目制任务，显示倒计时
- 🎯 **优先级系统**：高/中/低三级优先级，颜色区分
- 📝 **子任务**：支持为每个任务添加子任务，显示完成进度
- 🔍 **搜索功能**：快速搜索任务标题、描述、标签
- 📌 **任务模板**：内置常用模板，一键创建任务

### 📊 数据统计
- 今日任务完成率
- 总体任务统计
- 连续完成天数
- 每日趋势图表

### 🔄 云同步
- **Supabase 后端**：数据安全存储在云端
- **多设备同步**：在不同电脑间同步任务数据
- **记住登录**：勾选"记住我"后自动保存账号密码
- **离线支持**：断网时正常使用，联网后自动同步

### 🎮 升级系统（v4.2.2 全新）
- ⚡ **等级与经验**：完成任务/番茄钟/每日挑战赚取经验，经验条实时增长，升级获得称号（初出茅庐 → 传说冒险者）
- 🎉 **升级特效**：升级瞬间粒子爆炸 + 横幅提示新称号
- 🍅 **番茄钟**：侧边栏迷你番茄 + 完整模式专注页（25 分钟专注、任务绑定、专注统计）
- 🔥 **连击系统**：连续完成每日任务累积连击，断一天清零，连击发额外经验
- 🏅 **成就徽章**：首批 20 枚徽章（任务/专注/连击/单日/彩蛋），图鉴面板实时解锁
- 🎯 **每日挑战**：每天 3 个随机挑战（完成任务/番茄/按时截止），达成得经验
- 📈 **成长曲线**：统计页展示近 7 天经验走势
- ☁️ **成长数据同步**：经验流水/番茄会话上传云端，用户档案跨设备按总经验合并

### 🎯 其他功能
- 🌓 **主题切换**：亮色/暗色/跟随系统
- ⌨️ **全局快捷键**：
  - `Ctrl+Alt+T`：切换侧边栏/完整模式
  - `Ctrl+N`：新建任务
  - `Ctrl+F`：搜索
- 🎨 **毛玻璃效果**：半透明亚克力背景
- ✨ **流畅动画**：任务添加/完成/删除动画
- 🔔 **通知提醒**：截止任务即将到期时提醒
- 📤 **数据导出**：支持导出为 JSON/CSV 格式
- 🕐 **实时时间**：侧边栏显示当前日期、时间和星期

## 📸 界面预览

### 侧边栏模式
- 贴边显示，不干扰工作
- 鼠标悬停自动展开
- 快速添加任务
- 实时显示日期时间

### 完整模式
- 标签页切换（每日/截止/历史/统计/专注）
- 任务详情编辑
- 数据统计图表
- 专注页番茄钟 + 每日挑战面板

## 🚀 快速开始

### 安装方式

1. 下载最新版本的安装包：[Releases](https://github.com/hsong6809-boop/TodoSidebar/releases)
2. 运行 `TodoSidebar-Setup-v4.2.2.exe`
3. 按照向导完成安装

### 首次使用

1. 启动应用后会显示登录界面
2. 勾选"记住我"可保存账号密码
3. 点击"注册"创建账号（需要邮箱验证）
4. 登录后即可开始使用

### 云同步设置

1. 应用使用 Supabase 作为后端，数据自动同步到云端
2. 在不同电脑上登录同一账号即可同步数据
3. 默认每 30 秒自动同步一次
4. 也可手动点击同步按钮上传/下载

## 🛠️ 开发环境

### 技术栈

- **前端框架**：WPF (.NET 8.0)
- **MVVM 框架**：CommunityToolkit.Mvvm
- **数据库**：SQLite (本地) + Supabase (云端)
- **依赖注入**：Microsoft.Extensions.DependencyInjection
- **编程语言**：C#

### 项目结构

```
TodoSidebar/
├── App.xaml(.cs)              # 应用入口 + DI 配置
├── MainWindow.xaml(.cs)       # 侧边栏模式
├── FullWindow.xaml(.cs)       # 完整模式
├── LoginWindow.xaml(.cs)      # 登录窗口
├── SettingsWindow.cs          # 设置窗口
├── UpgradeDialog.xaml(.cs)    # 升级对话框
├── Config/
│   └── SupabaseConfig.cs     # Supabase 配置
├── Models/
│   └── TaskItem.cs           # 任务模型
├── Interfaces/                # 服务接口定义
├── Helpers/
│   └── BlurHelper.cs         # 毛玻璃效果辅助
├── Services/
│   ├── AuthService.cs        # 认证服务
│   ├── DatabaseService.cs    # 数据库服务（WAL 模式 + 并发锁）
│   ├── SyncService.cs        # 同步服务
│   ├── SyncLogService.cs     # 同步日志服务
│   ├── TaskService.cs        # 任务服务
│   ├── AnimationService.cs   # 动画服务（硬件加速）
│   ├── NotificationService.cs # 通知提醒服务
│   ├── ExportService.cs      # 数据导出服务
│   ├── ThemeManager.cs       # 主题管理
│   ├── HotkeyService.cs      # 全局快捷键
│   ├── NetworkMonitor.cs     # 网络状态监控
│   ├── FeatureFlagService.cs # 功能开关服务
│   └── LicenseService.cs     # 授权服务骨架
├── ViewModels/
│   ├── MainViewModel.cs      # 主视图模型
│   ├── SyncViewModel.cs      # 同步视图模型
│   └── StatisticsViewModel.cs # 统计视图模型
├── TodoSidebar.Tests/         # 测试项目
└── TodoSidebar.iss            # Inno Setup 安装脚本
```

### 本地开发

1. 克隆项目
```bash
git clone https://github.com/hsong6809-boop/TodoSidebar.git
cd TodoSidebar
```

2. 使用 Visual Studio 2022 打开 `TodoSidebar.sln`

3. 还原 NuGet 包
```bash
dotnet restore
```

4. 运行项目
```bash
dotnet run
```

### 构建发布版本

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o bin\publish
```

### 创建安装包

1. 安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)
2. 打开 `TodoSidebar.iss`
3. 点击 编译 → 编译

## 📦 依赖项

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM 框架
- [Microsoft.Data.Sqlite](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/) - SQLite 数据库
- [Microsoft.Extensions.DependencyInjection](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) - 依赖注入
- [Supabase](https://github.com/supabase-community/supabase-csharp) - Supabase C# 客户端

## 🔧 配置说明

### Supabase 配置

在 `Config/SupabaseConfig.cs` 中配置 Supabase 连接信息：

```csharp
public static string Url { get; set; } = "https://your-project.supabase.co";
public static string AnonKey { get; set; } = "your-anon-key";
```

### 数据库初始化

在 Supabase 控制台的 SQL Editor 中执行 `Database/init.sql` 脚本。

## 📝 更新日志

### v4.2.2 (2026-08-06)
- 🎮 **升级系统上线**（P1~P5 全部完成）：
  - ⚡ 等级/经验/称号系统：完成任务、番茄钟、连击、每日挑战赚经验，经验条实时刷新，升级粒子特效 + 横幅
  - 🍅 番茄钟：侧边栏迷你番茄 + 完整模式专注页，任务绑定、专注统计、XP 联动
  - 🔥 连击系统：每日任务全清续连击，断一天清零，连击经验加成
  - 🏅 成就徽章：首批 20 枚（任务/专注/连击/单日/彩蛋）+ 图鉴面板
  - 🎯 每日挑战：每日 3 个随机挑战 + 挑战面板
  - 📈 成长曲线：近 7 天经验折线图
  - ☁️ 成长数据云同步：XP 流水/番茄会话上传 + 用户档案跨设备合并
- 🗂️ **完整模式**：移除模板页，新增专注页；统计标题优化；顶栏等级框布局修复
- 🐛 **修复**：经验条不刷新、番茄环形重叠、顶栏按钮被等级框挤开、专注页跑到 Tab 栏上方等问题
- 🛡️ **凭据安全**：Supabase 凭据改为环境变量 / AppData 配置文件加载，代码仓库不再内置

### v4.1.0 (2026-06-11)
- 🏗️ **架构精简**：删除 TodoSidebar.Core 和 TodoSidebar.Services 子项目，所有代码合并到主项目，减少 2100+ 行冗余代码
- 🐛 **修复侧边栏悬浮失效**：修复 5 层叠加 Bug（动画属性回退、触发条透明像素不可交互、DPI 坐标不匹配、定时器芝诺悖论、状态切换定时器遗漏）
- 🐛 **修复触发条交互**：背景从渐变改为纯色，解决 `AllowsTransparency=True` 下透明像素不响应鼠标事件
- 🐛 **修复定时器芝诺悖论**：鼠标轮询定时器不再重复重置悬停延迟定时器，改为 `if (!IsEnabled) Start()` 模式
- ⚡ **数据库优化**：启用 WAL 模式 + SemaphoreSlim 并发锁，提升读写性能；损坏数据库先备份再重建
- ⚡ **动画服务优化**：统一 TransformGroup 管理，新增硬件加速缓存
- ⚡ **统计性能优化**：单次遍历计算所有统计指标，移除冗余 TaskService 依赖
- ✨ **通知增强**：新增零点定时器自动清空已通知列表
- 🧹 **资源清理**：App.OnExit 完善事件处理器注销和数据库连接释放

### v4.0.0 (2026-06-10)
- ✨ **每日任务逻辑重构**：建立一次，每天自动刷新，完成状态按天独立记录
- ✨ **登录记住我**：勾选后自动保存账号密码，下次启动自动填充
- ✨ **侧边栏实时时间**：显示当前日期、时间和星期
- 🎨 **设置页面重做**：毛玻璃圆角卡片风格，与应用主题统一
- 🎨 **应用标题统一**：改为"每日任务"
- 🐛 **修复同步按钮**：上传/下载按钮绑定路径修正
- 🐛 **修复设置窗口崩溃**：PrimaryBrush 资源不存在导致崩溃
- 🏗️ **架构重构**：拆分为 Core/Services/App/Tests 四层架构
- 🏗️ **依赖注入**：引入 Microsoft.Extensions.DependencyInjection
- 🏗️ **接口抽象**：所有服务通过接口访问，可 mock 可测试
- 🏗️ **商业化骨架**：ILicenseService + IFeatureFlagService 就位（不限制功能）

### v3.2.1 (2026-06-09)
- ✨ 新增手动同步按钮（上传/下载）
- ✨ 新增今日已完成任务显示
- ✨ 新增卸载时询问是否保留数据
- 🎨 侧边栏底部显示今日已完成任务
- 🎨 同步按钮同时显示在侧边栏和完整模式
- 🐛 修复 UI 线程死锁导致界面不显示的问题

### v3.2.0 (2026-06-09)
- ✨ 新增 Supabase 云同步功能
- ✨ 新增记住登录功能
- ✨ 新增退出登录按钮
- 🎨 优化 UI 设计，使用 Tailwind CSS 色板
- 🐛 修复登录后 DataContext 为 null 的问题
- 🐛 修复安装后首次启动不显示登录界面的问题

### v3.0.0
- ✨ 新增双模式界面（侧边栏+完整窗口）
- ✨ 新增子任务支持
- ✨ 新增任务模板系统
- ✨ 新增数据统计功能
- 🎨 全新 UI 设计，支持毛玻璃效果

## 📄 许可证

## 📜 开源协议

本项目采用 [BSL 1.1](LICENSE)（Business Source License）协议。

**简单来说：**
- ✅ **免费使用**：个人、学习、研究、非商业用途完全免费
- ✅ **自由修改**：可以修改源码用于个人使用
- ✅ **自由分享**：可以分享给他人用于非商业用途
- ❌ **禁止商业竞品**：不能将本软件作为商业产品销售或提供付费服务
- ⏰ **自动开源**：4 年后（2030年）自动转为 Apache 2.0 完全开源协议

**如果你需要商业使用**（如企业内部大规模部署、提供 SaaS 服务等），请联系获取商业授权。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📧 联系方式

如有问题或建议，请在 GitHub 上提交 Issue。

---

⭐ 如果觉得这个项目有用，请给个 Star 支持一下！
