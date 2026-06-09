# TodoSidebar 功能优化实现计划

> **目标:** 实现 18 项功能优化，提升用户体验和视觉效果

**技术栈:** .NET 8.0 WPF, SQLite, CommunityToolkit.Mvvm

---

## 阶段一：视觉效果优化 (1-6)

### Task 1: 深色模式支持
- 创建 ThemeManager 服务
- 添加深色主题资源字典
- 实现主题切换 UI
- 保存用户偏好到数据库

### Task 2: 卡片样式优化
- 添加阴影效果
- 优化圆角和边距
- 添加悬停状态

### Task 3: 优先级可视化增强
- 左边框颜色区分
- 优先级图标（🔴🟡🟢）
- 紧急任务闪烁

### Task 4: 进度可视化
- 每日任务进度环
- 每周任务进度条
- 里程碑完成百分比

### Task 5: 动画增强
- 任务添加动画（滑入）
- 任务完成动画（划线+粒子）
- 任务删除动画（滑出）

### Task 6: 交互反馈
- 按钮悬停效果
- 点击反馈
- 错误提示动画

---

## 阶段二：核心功能增强 (7-12)

### Task 7: 布局优化
- 统计概览区域
- 搜索框
- 分组折叠

### Task 8: 任务搜索功能
- 实时搜索
- 高亮匹配
- 筛选器

### Task 9: 全局快捷键
- Ctrl+Alt+T: 唤出侧边栏
- Ctrl+N: 快速添加
- Ctrl+F: 搜索

### Task 10: 键盘导航
- 上下箭头选择
- Enter 完成
- Delete 删除

### Task 11: 任务详情扩展
- 描述/备注
- 子任务/清单
- 标签系统

### Task 12: 任务提醒系统
- Windows Toast 通知
- 定时提醒
- 到期预警

---

## 阶段三：数据管理 (13-18)

### Task 13: 任务拖拽排序
- 实现拖拽逻辑
- 保存排序顺序

### Task 14: 数据统计与可视化
- 完成趋势图
- 类型占比图
- 生产力评分

### Task 15: 数据管理
- 导出 JSON/CSV
- 导入功能
- 数据备份

### Task 16: 任务模板
- 保存为模板
- 从模板创建

### Task 17: 批量操作
- 全选功能
- 批量删除
- 批量完成

### Task 18: 自定义主题
- 主题色选择
- 背景设置
- 字体调节

---

## 文件结构

```
TodoSidebar/
├── Services/
│   ├── ThemeManager.cs          # 主题管理
│   ├── NotificationService.cs   # 通知服务
│   ├── ExportService.cs         # 导出服务
│   └── BackupService.cs         # 备份服务
├── Converters/
│   ├── PriorityToColorConverter.cs
│   ├── ProgressConverter.cs
│   └── BoolToAnimationConverter.cs
├── Controls/
│   ├── CircularProgress.cs      # 进度环控件
│   ├── TagControl.cs            # 标签控件
│   └── TaskCard.cs              # 增强卡片
├── Themes/
│   ├── LightTheme.xaml
│   └── DarkTheme.xaml
└── ViewModels/
    ├── StatisticsViewModel.cs   # 统计
    └── SettingsViewModel.cs     # 设置
```
