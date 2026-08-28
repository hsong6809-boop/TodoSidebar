# TodoSidebar 云同步 · Supabase 部署指南

> 2026-08 云同步排查后整理。**凡是切换/新建 Supabase 项目、或从旧版本升级到 v5.3+，都必须按本指南执行 SQL，否则任务上传会被云端拒绝（静默失败）。**

## 0. 客户端配置（每台机器）

优先级：环境变量 > `%APPDATA%\TodoSidebar\supabase.json` > exe 同目录 `supabase.json`。

- 环境变量：`SUPABASE_URL` / `SUPABASE_ANON_KEY`
- 配置文件格式见根目录 `supabase.example.json`（占位值，勿替换真实文件）

## 1. SQL 脚本执行顺序（Supabase Dashboard → SQL Editor → 逐段执行）

| 顺序 | 脚本 | 作用 | 必选 |
|---|---|---|---|
| ① | `supabase_setup.sql` | 建 xp_log / pomodoro_session / user_profile + RLS | ✅ |
| ② | `supabase_tasks_rls.sql` | 建 tasks 表（含 deleted_at、recurrence）+ RLS + 索引 | ✅ |
| ③ | `account_profile_setup.sql` | 建 account_profile（账号中心） | ✅ |
| ④ | `tasks_deleted_at_setup.sql` | 存量库补 deleted_at 列（②已含则跳过） | 仅旧库 |
| ⑤ | `tasks_recurrence_setup.sql` | 存量库补 recurrence 列（②已含则跳过） | 仅旧库 |

**存量库快速修复（客户端 v5.3+ 已在用、怀疑同步失灵）**：直接执行根目录
`supabase_v560_cloud_migration.sql` 一个文件即可（补列 + 索引 + 触发器体检）。

## 2. 三个关键体检项

```sql
-- a) 列齐全（应返回 deleted_at、recurrence 两行）
select column_name from information_schema.columns
where table_schema='public' and table_name='tasks' and column_name in ('deleted_at','recurrence');

-- b) RLS 全部开启（relrowsecurity 应全为 t）
select c.relname, c.relrowsecurity from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname='public' and c.relname in ('tasks','xp_log','pomodoro_session','user_profile','account_profile');

-- c) 旧触发器体检（存在 update_tasks_updated_at 时必须处理，详见 d）
select tgname from pg_trigger where tgrelid='public.tasks'::regclass and not tgisinternal;
```

```sql
-- d) 若 c 查出旧触发器：删除（旧版 Database/init.sql 遗留，会覆盖客户端真实编辑时间）。
--    或按 supabase_v560_cloud_migration.sql 内的替换语句改为"客户端携带更大值时保留客户端值"。
drop trigger if exists update_tasks_updated_at on public.tasks;
```

## 3. 常见故障对照

| 现象 | 原因 | 处理 |
|---|---|---|
| 任务改了不上云、本地一直"待同步"，无报错 | 云端缺 deleted_at/recurrence 列，上传被 PostgREST 整批拒绝 | 执行 `supabase_v560_cloud_migration.sql` |
| 登录/同步报错 `42703 / PGRST204 / column ... does not exist` | 同上；或客户端版本与云端 schema 不一致 | 对齐脚本 ② 后重跑 |
| 其他设备收不到某任务的修改 | 云端 updated_at 被旧触发器改写，LWW 判定失真 | 执行 2-d 删除/替换触发器 |
| 多账号数据串号 | RLS 未开或策略缺失 | 重跑 ② ，核对 2-b |
| 同步偶发失败后自动恢复 | 网络瞬时问题/500 | 属于正常重试路径，看 `sync_log.json` |

## 4. 安全注意

- 所有业务表都已 `enable row level security`，**anon key 泄露不构成数据读取/写入面**，但仍建议在上线前轮换一次（旧版本曾把 anon key 提交进 git 历史）。
- `supabase.json` 已加入 `.gitignore`；发布目录里的真实凭据文件复制时注意不要进仓库。