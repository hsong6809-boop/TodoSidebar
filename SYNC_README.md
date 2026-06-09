# TodoSidebar 联网同步功能

## 功能概述

TodoSidebar 支持通过 Supabase 进行云端同步，实现多设备数据同步。

## 设置步骤

### 1. 创建 Supabase 项目

1. 访问 [Supabase](https://supabase.com/) 并注册账号
2. 创建一个新项目
3. 等待项目初始化完成

### 2. 获取 API 配置

1. 在 Supabase 控制台，进入 `Settings` → `API`
2. 复制以下信息：
   - **Project URL**: `https://xxx.supabase.co`
   - **anon/public key**: 以 `eyJ...` 开头的密钥

### 3. 配置应用

编辑 `Config/SupabaseConfig.cs` 文件：

```csharp
public static string Url { get; set; } = "https://你的项目.supabase.co";
public static string AnonKey { get; set; } = "你的anon-key";
```

### 4. 初始化数据库

1. 在 Supabase 控制台，进入 `SQL Editor`
2. 复制 `Database/init.sql` 文件的内容
3. 粘贴并执行 SQL 脚本

### 5. 启用邮箱认证

1. 在 Supabase 控制台，进入 `Authentication` → `Providers`
2. 确保 `Email` 已启用
3. 可选：禁用邮箱确认（开发测试时）
   - 进入 `Authentication` → `Settings`
   - 关闭 `Enable email confirmations`

### 6. 启动应用

使用 `--sync` 参数启动应用以启用同步功能：

```bash
TodoSidebar.exe --sync
```

或创建快捷方式添加 `--sync` 参数。

## 功能特性

- ✅ 邮箱密码登录/注册
- ✅ 自动同步任务到云端
- ✅ 离线队列支持
- ✅ 冲突解决（Last Write Wins）
- ✅ 定时自动同步（30秒间隔）

## 文件结构

```
TodoSidebar/
├── Config/
│   └── SupabaseConfig.cs      # Supabase 配置
├── Services/
│   ├── AuthService.cs         # 认证服务
│   ├── SyncService.cs         # 同步服务
│   └── SupabaseClientService.cs # Supabase 客户端
├── Models/
│   └── SyncModels.cs          # 同步相关模型
├── Database/
│   └── init.sql               # 数据库初始化脚本
├── LoginWindow.xaml           # 登录窗口 UI
└── LoginWindow.xaml.cs        # 登录窗口逻辑
```

## 待完成功能

- [ ] 完整的离线队列实现
- [ ] 冲突解决 UI
- [ ] 同步状态指示器
- [ ] 多设备管理
- [ ] 数据加密

## 注意事项

1. **安全性**: 生产环境请使用环境变量存储 API 密钥
2. **网络**: 需要能访问 Supabase 服务（可能需要代理）
3. **数据**: 免费套餐限制 500MB 存储空间
