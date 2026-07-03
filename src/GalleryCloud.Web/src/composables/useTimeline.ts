import { ref, computed } from 'vue'
import client from '../api/client'
import type { PhotoIdItem } from '../types'

export interface DayDensity {
  date: string           // "2026-07-02"
  count: number
  ids: PhotoIdItem[] | null  // null = 未加载
  loading: boolean
  loaded: boolean
}

export type RowItem = {
  type: 'header'
  label: string
  date: string
  id: string
} | {
  type: 'row'
  photos: PhotoIdItem[]
  dayKeys: string[]      // 对应的日期列表（用于视口驱动加载）
  loading: boolean        // true = 还有未加载的日
  estimatedCount?: number // 未加载时的估算照片数（防闪烁）
  id: string
}

export function useTimeline() {
  const dayDensities = ref<DayDensity[]>([])
  const nullDateItems = ref<PhotoIdItem[]>([])
  const nullDateLoaded = ref(false)
  const loading = ref(true)
  const error = ref('')
  const earliestYear = ref(0)
  const latestYear = ref(0)

  // 所有已加载的照片（用于照片查看器导航）
  const allLoadedItems = computed<PhotoIdItem[]>(() => {
    const result: PhotoIdItem[] = []
    for (const dd of dayDensities.value) {
      if (dd.ids) result.push(...dd.ids)
    }
    if (nullDateItems.value.length > 0) result.push(...nullDateItems.value)
    return result
  })

  // O(1) 日期查找
  const dayMap = computed(() => {
    const map = new Map<string, DayDensity>()
    for (const dd of dayDensities.value) {
      map.set(dd.date, dd)
    }
    return map
  })

  const totalPhotos = computed(() => {
    let total = 0
    for (const dd of dayDensities.value) total += dd.count
    return total + nullDateItems.value.length
  })

  async function initDensities() {
    loading.value = true
    try {
      const [densityRes, yearsRes] = await Promise.all([
        client.get('/timeline/daily-density', { params: { direction: 'desc' } }),
        client.get('/timeline/years'),
      ])

      const raw = densityRes.data as { date: string; count: number }[]
      dayDensities.value = raw.map((d: any) => ({
        date: d.date,
        count: d.count,
        ids: null,
        loading: false,
        loaded: false,
      }))

      const years = yearsRes.data as { year: number; count: number }[]
      if (years.length > 0) {
        latestYear.value = years[0].year      // 最新优先
        earliestYear.value = years[years.length - 1].year
      }
    } catch (e: any) {
      error.value = e.message || '加载失败'
    } finally {
      loading.value = false
    }
  }

  async function loadDayIds(date: string) {
    const dd = dayMap.value.get(date)
    if (!dd || dd.loaded || dd.loading) return

    dd.loading = true
    try {
      const res = await client.get('/timeline/date-ids', { params: { date } })
      const data = res.data as { items: { id: string; dateInt: number | null }[] }
      dd.ids = data.items.map(item => ({ id: item.id, takenAtDate: item.dateInt ?? null }))
      dd.loaded = true
    } catch {
      // 单个日期加载失败不影响整体
    } finally {
      dd.loading = false
    }
  }

  async function loadNullDateIds() {
    if (nullDateLoaded.value) return
    try {
      const res = await client.get('/timeline/null-date-ids')
      const ids = res.data as string[]
      nullDateItems.value = ids.map(id => ({ id, takenAtDate: null }))
    } catch { /* */ }
    nullDateLoaded.value = true
  }

  // 从 dayDensities 构建虚拟行（不依赖实际 IDs）
  function buildRows(columns: number, groupLevel: 'day' | 'month' | 'none'): RowItem[] {
    const rows: RowItem[] = []
    if (columns < 1) return rows

    if (groupLevel === 'none') {
      // 平铺模式：直接在已加载的数据上构建
      const items = allLoadedItems.value
      if (items.length === 0) {
        // 还没加载任何数据时，用 density counts 估算行数
        const totalCount = dayDensities.value.reduce((s, d) => s + d.count, 0)
        const estRows = Math.max(1, Math.ceil(totalCount / columns))
        for (let i = 0; i < estRows; i++) {
          rows.push({
            type: 'row',
            photos: [],
            dayKeys: dayDensities.value.map(d => d.date),
            loading: true,
            estimatedCount: i < estRows - 1 ? columns : (totalCount % columns || columns),
            id: `r-est-${i}`,
          })
        }
      } else {
        for (let i = 0; i < items.length; i += columns) {
          const chunk = items.slice(i, i + columns)
          rows.push({
            type: 'row',
            photos: chunk,
            dayKeys: [],
            loading: false,
            id: `r-${rows.length}`,
          })
        }
      }
      return rows
    }

    if (groupLevel === 'day') {
      for (const dd of dayDensities.value) {
        const y = parseInt(dd.date.substring(0, 4))
        const m = parseInt(dd.date.substring(5, 7))
        const d = parseInt(dd.date.substring(8, 10))
        const label = `${y}年${m}月${d}日`

        rows.push({ type: 'header', label, date: dd.date, id: `h-${dd.date}` })

        const rowCount = Math.max(1, Math.ceil(dd.count / columns))
        const loaded = dd.ids !== null

        for (let i = 0; i < rowCount; i++) {
          const start = i * columns
          const photos = loaded ? dd.ids!.slice(start, start + columns) : []
          // 未加载时用估计数量（最后一行可能不满 columns）
          const estimatedPhotos = loaded ? photos.length : (i < rowCount - 1 ? columns : (dd.count % columns || columns))
          rows.push({
            type: 'row',
            photos,
            dayKeys: [dd.date],
            loading: !dd.loaded,
            estimatedCount: estimatedPhotos,
            id: `r-${dd.date}-${i}`,
          })
        }
      }
    } else {
      // month 模式
      let monthKey = ''
      let monthLabel = ''
      let monthDays: DayDensity[] = []
      let monthTotal = 0

      function flushMonth() {
        if (monthDays.length === 0) return
        rows.push({ type: 'header', label: monthLabel, date: monthKey, id: `h-${monthKey}` })

        const rowCount = Math.max(1, Math.ceil(monthTotal / columns))
        const allLoaded = monthDays.every(d => d.loaded)
        let flatPhotos: PhotoIdItem[] = []
        if (allLoaded) {
          for (const md of monthDays) {
            if (md.ids) flatPhotos.push(...md.ids)
          }
        }

        for (let i = 0; i < rowCount; i++) {
          const start = i * columns
          const photos = allLoaded ? flatPhotos.slice(start, start + columns) : []
          const estimatedPhotos = allLoaded ? photos.length : (i < rowCount - 1 ? columns : (monthTotal % columns || columns))
          rows.push({
            type: 'row',
            photos,
            dayKeys: monthDays.map(d => d.date),
            loading: !allLoaded,
            estimatedCount: estimatedPhotos,
            id: `r-${monthKey}-${i}`,
          })
        }

        monthDays = []
        monthTotal = 0
      }

      for (const dd of dayDensities.value) {
        const mk = dd.date.substring(0, 7)
        if (mk !== monthKey && monthKey !== '') flushMonth()
        monthKey = mk
        const y = parseInt(dd.date.substring(0, 4))
        const m = parseInt(dd.date.substring(5, 7))
        monthLabel = `${y}年${m}月`
        monthDays.push(dd)
        monthTotal += dd.count
      }
      flushMonth()
    }

    // 无日期照片：追加到末尾
    if (nullDateItems.value.length > 0) {
      rows.push({ type: 'header', label: '日期未知', date: '', id: 'h-null' })
      const items = nullDateItems.value
      for (let i = 0; i < items.length; i += columns) {
        rows.push({
          type: 'row',
          photos: items.slice(i, i + columns),
          dayKeys: ['__null__'],
          loading: false,
          id: `r-null-${i}`,
        })
      }
    }

    return rows
  }

  return {
    dayDensities,
    allLoadedItems,
    dayMap,
    loading,
    error,
    earliestYear,
    latestYear,
    totalPhotos,
    nullDateItems,
    init: initDensities,
    loadDayIds,
    loadNullDateIds,
    buildRows,
  }
}
