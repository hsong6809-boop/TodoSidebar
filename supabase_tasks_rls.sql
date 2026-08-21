-- ============================================================
-- TodoSidebar RLS 体检 + tasks 表 RLS 兜底（幂等，可重复执行）
-- 背景：旧 anon key 曾进入 git 历史。anon key 的安全性完全依赖 RLS。
-- 只要所有业务表都开了 RLS，anon key 泄露就无法读写数据。
-- ============================================================

-- ========== 1. 体检：查看全部业务表 RLS 状态 ==========
-- relrowsecurity=t 表示已开启 RLS，f 表示未开启（危险）
select c.relname as table_name,
       c.relrowsecurity as rls_enabled,
       (select count(*) from pg_policies p where p.schemaname='public' and p.tablename=c.relname) as policy_count
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and c.relkind = 'r'
  and c.relname in ('tasks','xp_log','pomodoro_session','user_profile')
order by c.relname;

-- ========== 2. tasks 表兜底：建表（幂等）+ 开启 RLS + 策略 ==========
-- M13 修复：tasks 表的建表语句此前不在仓库中，全新环境执行到 alter table 会报错中断，
-- 后续 4 条策略全部不创建。此处按 SyncTask 模型补齐幂等建表语句。
create table if not exists public.tasks (
    id            uuid primary key default gen_random_uuid(),
    user_id       text,
    title         text not null,
    type          integer not null default 0,                -- 0=Daily, 1=Deadline
    priority      integer not null default 1,                -- 0=Low, 1=Med, 2=High
    is_completed  boolean not null default false,
    created_at    timestamptz not null default now(),
    deadline      timestamptz,
    completed_at  timestamptz,
    description   text,
    tags          text,
    sort_order    integer not null default 0,
    subtasks_json text,
    updated_at    timestamptz not null default now(),
    is_deleted    boolean not null default false
);

create index if not exists tasks_user_updated_idx on public.tasks (user_id, updated_at);

alter table public.tasks enable row level security;

drop policy if exists "tasks_select_own" on public.tasks;
drop policy if exists "tasks_insert_own" on public.tasks;
drop policy if exists "tasks_update_own" on public.tasks;
drop policy if exists "tasks_delete_own" on public.tasks;

create policy "tasks_select_own" on public.tasks
    for select using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "tasks_insert_own" on public.tasks
    for insert with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "tasks_update_own" on public.tasks
    for update using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')))
    with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "tasks_delete_own" on public.tasks
    for delete using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));

-- ========== 3. 复查（应全部 rls_enabled=t） ==========
select c.relname as table_name,
       c.relrowsecurity as rls_enabled,
       (select count(*) from pg_policies p where p.schemaname='public' and p.tablename=c.relname) as policy_count
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and c.relkind = 'r'
  and c.relname in ('tasks','xp_log','pomodoro_session','user_profile')
order by c.relname;
