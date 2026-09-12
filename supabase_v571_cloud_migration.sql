-- ============================================================
-- TodoSidebar v5.7.1 云同步补列迁移（幂等，可重复执行）
-- 使用：Supabase Dashboard → SQL Editor → 粘贴全部执行
--
-- 背景（v5.7.1 审查 M20）：
--   tasks 表缺少 estimated_minutes / actual_minutes 两列，而客户端
--   SyncTask 模型自本版起会序列化这两个键（null 也带上）。
--   PostgREST 对未知列整批拒绝（PGRST204/42703），会导致所有任务上传静默失败。
--   本脚本补齐列并恢复上传通道；执行后脏数据会在 30 秒内自动补传。
-- ============================================================

-- 1. 补列（幂等）
alter table public.tasks add column if not exists estimated_minutes integer;
alter table public.tasks add column if not exists actual_minutes integer;

-- 2. user_id 非空约束（NOT VALID：只约束新增/更新行，不阻断存量历史行）
alter table public.tasks drop constraint if exists tasks_user_id_not_null;
alter table public.tasks add constraint tasks_user_id_not_null check (user_id is not null) not valid;

-- 3. updated_at 触发器体检（同 v5.6 迁移；确保客户端真实编辑时间不被改写）
create or replace function update_updated_at_column()
returns trigger as $$
begin
    if new.updated_at is null or new.updated_at = old.updated_at then
        new.updated_at = now();
    end if;
    return new;
end;
$$ language 'plpgsql';

-- ============================================================
-- 验证（应返回两行）
-- ============================================================
select column_name from information_schema.columns
where table_schema = 'public' and table_name = 'tasks'
  and column_name in ('estimated_minutes', 'actual_minutes')
order by column_name;
