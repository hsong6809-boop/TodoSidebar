# 每日任务 (TodoSidebar)

一款现代化的 Windows 桌面待办管理应用，支持**侧边栏**和**完整窗口**双模式，带有 Supabase 云同步功能。

## ✨ 功能特性

### 🎨 双模式界面
- **侧边栏模式**：贴边显示，不占用工作区域，鼠标悬停自动展开/收起
- **完整模式**：全功能界面，支持标签页切换

### 📋 任务管理
- ✅ **每日任务**：每天重复的待办事项
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
- **记住登录**：首次登录后自动保持登录状态
- **离线支持**：断网时正常使用，联网后自动同步

### 🎯 其他功能
- 🌓 **主题切换**：亮色/暗色主题
- ⌨️ **全局快捷键**：
  - `Ctrl+Alt+T`：切换侧边栏/完整模式
  - `Ctrl+N`：新建任务
  - `Ctrl+F`：搜索
- 🎨 **毛玻璃效果**：半透明亚克力背景
- ✨ **流畅动画**：任务添加/完成/删除动画
- 🔔 **通知提醒**：截止任务即将到期时提醒
- 📤 **数据导出**：支持导出为 JSON/CSV 格式

## 📸 界面预览

### 侧边栏模式
- 贴边显示，不干扰工作
- 鼠标悬停自动展开
- 快速添加任务

### 完整模式
- 标签页切换（每日/截止/模板/历史/统计）
- 任务详情编辑
- 数据统计图表

## 🚀 快速开始

### 安装方式

1. 下载最新版本的安装包：[Releases](https://github.com/hsong6809-boop/TodoSidebar/releases)
2. 运行 `每日任务-Setup-3.2.0.exe`
3. 按照向导完成安装

### 首次使用

1. 启动应用后会显示登录界面
2. 点击"注册"创建账号（需要邮箱验证）
3. 登录后即可开始使用

### 云同步设置

1. 应用使用 Supabase 作为后端，数据自动同步到云端
2. 在不同电脑上登录同一账号即可同步数据
3. 默认每 30 秒自动同步一次

## 🛠️ 开发环境

### 技术栈

- **前端框架**：WPF (.NET 8.0)
- **MVVM 框架**：CommunityToolkit.Mvvm
- **数据库**：SQLite (本地) + Supabase (云端)
- **编程语言**：C#

### 项目结构

```
TodoSidebar/
├── App.xaml(.cs)              # 应用入口
├── MainWindow.xaml(.cs)       # 侧边栏模式
├── FullWindow.xaml(.cs)       # 完整模式
├── LoginWindow.xaml(.cs)      # 登录窗口
├── TaskDetailDialog.xaml(.cs) # 任务详情对话框
├── Config/
│   └── SupabaseConfig.cs     # Supabase 配置
├── Models/
│   ├── TaskItem.cs           # 任务模型
│   └── SyncModels.cs         # 同步模型
├── Services/
│   ├── AuthService.cs        # 认证服务
│   ├── DatabaseService.cs    # 数据库服务
│   ├── SyncService.cs        # 同步服务
│   ├── TaskService.cs        # 任务服务
│   └── ...                   # 其他服务
├── ViewModels/
│   ├── MainViewModel.cs      # 主视图模型
│   └── StatisticsViewModel.cs # 统计视图模型
├── Database/
│   └── init.sql              # Supabase 初始化脚本
└── TodoSidebar.iss           # Inno Setup 安装脚本
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
### v3.2.0 (2026-06-09)
 - ✨ 新增手动同步按钮（上传/下载）
 - ✨ 新增今日已完成任务显示
 - ✨ 新增卸载时询问是否保留数据
 - 🎨 侧边栏底部显示今日已完成任务
 - 🎨 同步按钮同时显示在侧边栏和完整模式
 - 🐛 修复 UI 线程死锁导致界面不显示的问题
 - 
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
