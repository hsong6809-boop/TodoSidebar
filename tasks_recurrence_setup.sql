-- ============================================================
-- TodoSidebar v5.4 重复任务：tasks 表补 recurrence 列（幂等版）
-- 使用方法：Supabase Dashboard → SQL Editor → 粘贴全部执行
-- 说明：
--   recurrence 为规则编码文本：daily / weekdays / weekly:N(1=周一…7=周日) / monthly；
--   NULL = 不重复。老客户端遇到未知列自动忽略，完全兼容。
--   未执行本脚本时新客户端的重复规则仅保存在本地、不上云。
-- ============================================================

alter table public.tasks add column if not exists recurrence text;

-- ========== 验证 ==========
-- select column_name from information_schema.columns
-- where table_schema='public' and table_name='tasks' and column_name='recurrence';
