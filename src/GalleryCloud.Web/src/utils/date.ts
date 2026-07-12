/**
 * 安全解析服务端返回的日期字符串。
 * SQLite 不保留 DateTimeKind，EF Core 读出后 JSON 序列化不带 Z，
 * 导致 new Date() 可能错误地当作本地时间解析。
 * 此处统一处理：无时区信息时视为 UTC。
 */
export function parseDateSafe(dateStr: string): Date {
  if (dateStr.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(dateStr)) {
    return new Date(dateStr)
  }
  // 无时区 → 视为 UTC
  return new Date(dateStr + 'Z')
}

/** 格式化为 YYYY-MM-DD HH:mm[:ss]（本地时间） */
export function formatLocalDateTime(dateStr: string | null, includeSeconds = false): string {
  if (!dateStr) return ''
  const d = parseDateSafe(dateStr)
  if (isNaN(d.getTime())) return dateStr
  const pad = (n: number) => n.toString().padStart(2, '0')
  if (includeSeconds) {
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
  }
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
