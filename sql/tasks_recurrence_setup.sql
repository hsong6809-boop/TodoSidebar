-- ============================================================
-- TodoSidebar v5.4 重复任务：tasks 表补 recurrence 列（幂等版）
-- 使用方法：Supabase Dashboard → SQL Editor → 粘贴全部执行
-- 说明：
--   recurrence 为规则编码文本：daily / weekdays / weekly:N(1=周一…7=周日) / monthly；
--   NULL = 不重复。老客户端遇到未知列自动忽略，完全兼容。
--   ⚠️ 必须执行！客户端 v5.6.0 每次任务 upsert 都会序列化该键（含 null），
--   云端缺列时 PostgREST 会整批拒绝（42703/PGRST204），导致所有任务上传失败
--   （2026-08 云同步排查实证）。与 supabase_v560_cloud_migration.sql 等价，任选其一。
-- ============================================================

alter table public.tasks add column if not exists recurrence text;

-- ========== 验证 ==========
-- select column_name from information_schema.columns
-- where table_schema='public' and table_name='tasks' and column_name='recurrence';
