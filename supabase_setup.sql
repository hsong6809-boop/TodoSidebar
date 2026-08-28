-- ============================================================
-- TodoSidebar 升级系统 Supabase 表结构 + RLS 策略（幂等版 v2）
-- 使用方法：Supabase Dashboard → SQL Editor → 粘贴全部执行
-- 特点：可重复执行。已存在的表/策略自动跳过，不会报错。
-- 说明：
--   1) user_id 使用 text 类型（与 supabase-csharp 的 User.Id 对齐）
--   2) RLS 策略同时兼容带连字符 uuid（auth.uid()::text）与
--      无连字符 uuid（replace(auth.uid()::text,'-','')）两种格式
--   3) 同步为"尽力而为"：若未建表，应用仅记录日志，不影响本地功能
-- ============================================================

-- ========== 1. xp_log（经验流水） ==========
create table if not exists public.xp_log (
    id          uuid primary key default gen_random_uuid(),
    user_id     text,
    source      text not null,                 -- task_complete / pomodoro / combo / challenge_*
    amount      integer not null default 0,
    task_id     integer,                       -- 本地任务 ID（仅本机语义）
    date        text not null,                 -- yyyy-MM-dd
    created_at  timestamptz not null default now()
);

alter table public.xp_log enable row level security;

drop policy if exists "xp_log_select_own" on public.xp_log;
drop policy if exists "xp_log_insert_own" on public.xp_log;
drop policy if exists "xp_log_update_own" on public.xp_log;
drop policy if exists "xp_log_delete_own" on public.xp_log;

create policy "xp_log_select_own" on public.xp_log
    for select using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "xp_log_insert_own" on public.xp_log
    for insert with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "xp_log_update_own" on public.xp_log
    for update using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')))
    with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "xp_log_delete_own" on public.xp_log
    for delete using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));

-- ========== 2. pomodoro_session（番茄会话） ==========
create table if not exists public.pomodoro_session (
    id               uuid primary key default gen_random_uuid(),
    user_id          text,
    task_id          integer,                  -- 绑定任务（本机语义）
    start_time       timestamptz not null default now(),
    end_time         timestamptz,
    duration_minutes integer not null default 25,
    completed        boolean not null default true,
    date             text not null             -- yyyy-MM-dd
);

alter table public.pomodoro_session enable row level security;

drop policy if exists "pomodoro_select_own" on public.pomodoro_session;
drop policy if exists "pomodoro_insert_own" on public.pomodoro_session;
drop policy if exists "pomodoro_update_own" on public.pomodoro_session;
drop policy if exists "pomodoro_delete_own" on public.pomodoro_session;

create policy "pomodoro_select_own" on public.pomodoro_session
    for select using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "pomodoro_insert_own" on public.pomodoro_session
    for insert with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "pomodoro_update_own" on public.pomodoro_session
    for update using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')))
    with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "pomodoro_delete_own" on public.pomodoro_session
    for delete using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));

-- ========== 3. user_profile（用户成长档案） ==========
create table if not exists public.user_profile (
    id              uuid primary key default gen_random_uuid(),
    user_id         text,
    level           integer not null default 1,
    xp              integer not null default 0,
    total_xp        integer not null default 0,
    combo_days      integer not null default 0,
    best_combo_days integer not null default 0,
    title           text not null default '初出茅庐',
    updated_at      timestamptz not null default now()
);

alter table public.user_profile enable row level security;

-- S5 修复：保证每个用户只有一行成长档案，使 upsert 语义正确。
-- 注意：若历史数据已产生重复行，需先按 user_id 去重后再执行本语句。
-- 去重预处理（有重复行时先执行；保留每个 user_id 最新一行）：
-- delete from public.user_profile a
-- using public.user_profile b
-- where a.user_id = b.user_id
--   and a.updated_at < b.updated_at;
create unique index if not exists user_profile_user_id_unique on public.user_profile(user_id);

drop policy if exists "user_profile_select_own" on public.user_profile;
drop policy if exists "user_profile_insert_own" on public.user_profile;
drop policy if exists "user_profile_update_own" on public.user_profile;
drop policy if exists "user_profile_delete_own" on public.user_profile;

create policy "user_profile_select_own" on public.user_profile
    for select using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "user_profile_insert_own" on public.user_profile
    for insert with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "user_profile_update_own" on public.user_profile
    for update using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')))
    with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "user_profile_delete_own" on public.user_profile
    for delete using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));

-- ========== 验证 ==========
-- 执行后可运行以下查询确认表与策略存在：
-- select tablename from pg_tables where schemaname='public' and tablename in ('xp_log','pomodoro_session','user_profile');
-- select policyname, tablename from pg_policies where schemaname='public' order by tablename, policyname;
