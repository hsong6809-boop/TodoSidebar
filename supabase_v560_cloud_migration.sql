-- ============================================================
-- TodoSidebar v5.6 云同步修复·合并迁移脚本（幂等，可重复执行）
-- 使用：Supabase Dashboard → SQL Editor → 粘贴全部执行
--
-- 背景（2026-08 云同步排查结论）：
--   tasks 表由旧版 Database/init.sql 创建，从未执行过 v5.3/v5.4 的补列脚本，
--   云端缺 deleted_at / recurrence 两列。而客户端 v5.6.0 每次 upsert 都会序列化
--   这两个键（null 也带上），PostgREST 对未知列整批拒绝（PGRST204/42703），
--   导致所有任务上传静默失败、本地脏数据堆积。
-- 本脚本补齐缺失列，恢复上传通道。执行后无需手工操作，脏数据会在 30 秒内自动补传。
-- ============================================================

-- 1. 补列（幂等）
alter table public.tasks add column if not exists deleted_at text;
alter table public.tasks add column if not exists recurrence text;

-- 2. 增量同步依赖的索引（幂等）
create index if not exists tasks_user_updated_idx on public.tasks (user_id, updated_at);

-- ============================================================
-- 3.（重要）updated_at 触发器体检
--    若你的 tasks 表存在旧版触发器 update_tasks_updated_at（由 Database/init.sql 创建），
--    它会把客户端上传的"真实编辑时间"强制改成服务端 now()，
--    导致 LWW 冲突解决失效（"谁最后联网谁赢"，较早编辑可能覆盖较新编辑）。
--    先执行下面这条查询确认：
-- ============================================================
select tgname from pg_trigger
where tgrelid = 'public.tasks'::regclass and not tgisinternal;

-- 若上一条查出了 update_tasks_updated_at，请取消注释并执行下面的替换语句，
-- 使"客户端显式携带了更新的 updated_at"时保留客户端值（仅未携带/相同时才用 now 兜底）：
-- create or replace function update_updated_at_column()
-- returns trigger as $$
-- begin
--     if new.updated_at is null or new.updated_at = old.updated_at then
--         new.updated_at = now();
--     end if;
--     return new;
-- end;
-- $$ language 'plpgsql';

-- ============================================================
-- 验证（应全部通过）
-- ============================================================
-- 1) 两列已存在：
select column_name from information_schema.columns
where table_schema = 'public' and table_name = 'tasks'
  and column_name in ('deleted_at', 'recurrence')
order by column_name;

-- 2) RLS 状态（应 relrowsecurity=t，即已开启行级安全）：
select c.relname as table_name, c.relrowsecurity as rls_enabled
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public' and c.relname in ('tasks','xp_log','pomodoro_session','user_profile')
order by c.relname;