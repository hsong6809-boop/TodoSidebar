-- ============================================================
-- TodoSidebar v5.3 回收站：tasks 表补 deleted_at 列（幂等版）
-- 使用方法：Supabase Dashboard → SQL Editor → 粘贴全部执行
-- 说明：
--   deleted_at 为软删除时间戳（客户端本地时间 ISO 格式文本），
--   仅作展示参考；删除判定仍以 is_deleted 布尔为准，与老客户端完全兼容。
--   未执行本脚本时新客户端照常工作（该列仅随 upsert 传输，云端缺列会报错——
--   因此建议尽快执行，使回收站元数据可跨设备一致）。
-- ============================================================

alter table public.tasks add column if not exists deleted_at text;

-- ========== 验证 ==========
-- select column_name from information_schema.columns
-- where table_schema='public' and table_name='tasks' and column_name='deleted_at';
