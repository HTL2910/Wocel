-- ═══════════════════════════════════════════════════════════════════════════
--  Wocel Office 1.0 — bảng nhật ký hoạt động
--  Chạy script này trong Supabase → SQL Editor trước khi bật ghi log từ xa.
--
--  Ứng dụng CHỈ gửi thông tin về thao tác (công cụ nào, tệp bao nhiêu byte,
--  mất bao lâu, lỗi gì). Nội dung tài liệu của người dùng không bao giờ rời máy.
-- ═══════════════════════════════════════════════════════════════════════════

create table if not exists public.wocel_activity_logs (
    id                text primary key,
    occurred_at       timestamptz not null default now(),
    received_at       timestamptz not null default now(),

    session_id        text,
    app_version       text,
    environment       text,
    device            text,
    os                text,

    level             text not null default 'info',   -- info | warning | error
    category          text not null default 'app',    -- app | pdf-tool | file | ui
    action            text,
    tool_id           text,

    file_name         text,
    file_ext          text,
    file_size_bytes   bigint,
    result_size_bytes bigint,
    page_count        integer,
    item_count        integer,

    duration_ms       bigint default 0,
    success           boolean not null default true,
    message           text,
    error_type        text,
    error_detail      text
);

-- Chỉ mục phục vụ việc truy vết lỗi
create index if not exists wocel_logs_time_idx    on public.wocel_activity_logs (occurred_at desc);
create index if not exists wocel_logs_session_idx on public.wocel_activity_logs (session_id);
create index if not exists wocel_logs_tool_idx    on public.wocel_activity_logs (tool_id, success);
create index if not exists wocel_logs_error_idx   on public.wocel_activity_logs (level) where level = 'error';

-- ── Bảo mật ───────────────────────────────────────────────────────────────
-- Ứng dụng desktop dùng khoá anon nên chỉ được phép GHI, không được phép ĐỌC.
alter table public.wocel_activity_logs enable row level security;

drop policy if exists "wocel app can insert logs" on public.wocel_activity_logs;
create policy "wocel app can insert logs"
    on public.wocel_activity_logs
    for insert
    to anon, authenticated
    with check (true);

-- Chỉ tài khoản service_role (bảng điều khiển Supabase của bạn) mới đọc được log.

-- ── Vài truy vấn hay dùng khi truy lỗi ────────────────────────────────────
-- 1) Lỗi trong 24 giờ qua
--    select occurred_at, app_version, tool_id, message, error_type
--    from public.wocel_activity_logs
--    where level = 'error' and occurred_at > now() - interval '24 hours'
--    order by occurred_at desc;

-- 2) Công cụ nào hay lỗi nhất
--    select tool_id,
--           count(*) filter (where not success) as loi,
--           count(*)                            as tong,
--           round(100.0 * count(*) filter (where not success) / count(*), 1) as ty_le_loi
--    from public.wocel_activity_logs
--    where tool_id is not null
--    group by tool_id order by ty_le_loi desc;

-- 3) Thao tác chậm (trên 5 giây)
--    select tool_id, file_ext, file_size_bytes, duration_ms, occurred_at
--    from public.wocel_activity_logs
--    where duration_ms > 5000 order by duration_ms desc limit 50;

-- 4) Dựng lại toàn bộ một phiên làm việc để tái hiện lỗi
--    select occurred_at, category, action, tool_id, success, message
--    from public.wocel_activity_logs
--    where session_id = '<dán session_id lấy từ dòng lỗi>'
--    order by occurred_at;

-- ── Dọn log cũ (tuỳ chọn) ─────────────────────────────────────────────────
-- delete from public.wocel_activity_logs where occurred_at < now() - interval '90 days';
