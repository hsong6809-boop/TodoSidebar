-- ============================================================
-- TodoSidebar v5.2 账号中心：account_profile 表 + RLS 策略（幂等版）
-- 使用方法：Supabase Dashboard → SQL Editor → 粘贴全部执行
-- 特点：可重复执行，已存在的表/策略自动跳过。
-- 说明：
--   1) user_id（auth uuid 文本）为主键，保证每账号一行；
--   2) uid 为 8 位数字短账号 ID（展示用），unique 兜底并发分配冲突；
--   3) avatar_kind: 'd1'~'d8' = 内置矢量头像；'custom' = 自定义，
--      自定义时 avatar_data 存 128px PNG 的 base64（约 10KB 量级）；
--   4) 未执行本脚本时应用自动降级为纯本地昵称/头像，不影响任何功能。
-- ============================================================

create table if not exists public.account_profile (
    user_id     text primary key,
    uid         text not null unique,
    nickname    text not null default '',
    avatar_kind text not null default 'd1',
    avatar_data text,
    updated_at  timestamptz not null default now()
);

alter table public.account_profile enable row level security;

drop policy if exists "account_profile_select_own" on public.account_profile;
drop policy if exists "account_profile_insert_own" on public.account_profile;
drop policy if exists "account_profile_update_own" on public.account_profile;
drop policy if exists "account_profile_delete_own" on public.account_profile;

create policy "account_profile_select_own" on public.account_profile
    for select using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "account_profile_insert_own" on public.account_profile
    for insert with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "account_profile_update_own" on public.account_profile
    for update using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')))
    with check (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));
create policy "account_profile_delete_own" on public.account_profile
    for delete using (user_id in (auth.uid()::text, replace(auth.uid()::text, '-', '')));

-- ========== 验证 ==========
-- select * from pg_tables where schemaname='public' and tablename='account_profile';
-- select policyname from pg_policies where tablename='account_profile';
