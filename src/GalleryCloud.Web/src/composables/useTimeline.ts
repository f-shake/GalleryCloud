import { ref, computed } from 'vue'
import client from '../api/client'
import type { PhotoIdItem } from '../types'

export type RowItem = {
  type: 'header'
  label: string
  date: string
  id: string
} | {
  type: 'row'
  photos: PhotoIdItem[]
  id: string
}

export function useTimeline() {
  const allItems = ref<PhotoIdItem[]>([])
  const loading = ref(true)
  const error = ref('')
  const earliestYear = ref(0)
  const latestYear = ref(0)

  async function init() {
    loading.value = true
    try {
      const res = await client.get('/photos/ids')
      const data = res.data as { ids: string[]; dates: (number | null)[] }
      allItems.value = data.ids.map((id, i) => ({ id, takenAtDate: data.dates[i] ?? null }))
      if (allItems.value.length > 0) {
        // Find first/last items with valid dates (nulls may be at edges)
        const firstWithDate = allItems.value.find(i => i.takenAtDate != null)
        let lastWithDate: PhotoIdItem | undefined
        for (let i = allItems.value.length - 1; i >= 0; i--) {
          if (allItems.value[i].takenAtDate != null) { lastWithDate = allItems.value[i]; break }
        }
        latestYear.value = firstWithDate?.takenAtDate != null ? Math.floor(firstWithDate.takenAtDate / 10000) : 0
        earliestYear.value = lastWithDate?.takenAtDate != null ? Math.floor(lastWithDate.takenAtDate / 10000) : 0
      }
    } catch (e: any) {
      error.value = e.message || '加载失败'
    } finally {
      loading.value = false
    }
  }

  // Build virtual rows with group headers
  function buildRows(columns: number, groupLevel: 'day' | 'month' | 'none'): RowItem[] {
    const rows: RowItem[] = []
    const items = allItems.value
    if (items.length === 0 || columns < 1) return rows

    let currentKey = ''
    let currentLabel = ''
    let currentBatch: PhotoIdItem[] = []

    function flushBatch() {
      if (currentBatch.length === 0) return
      if (groupLevel === 'none') {
        while (currentBatch.length > 0) {
          rows.push({ type: 'row', photos: currentBatch.splice(0, columns), id: `r-${rows.length}` })
        }
      } else {
        rows.push({ type: 'header', label: currentLabel, date: currentKey, id: `h-${currentKey}` })
        while (currentBatch.length > 0) {
          rows.push({ type: 'row', photos: currentBatch.splice(0, columns), id: `r-${rows.length}` })
        }
      }
      currentBatch = []
    }

    for (const item of items) {
      const d = item.takenAtDate
      if (d == null) {
        currentBatch.push(item)
        continue
      }
      const y = Math.floor(d / 10000)
      const m = Math.floor((d % 10000) / 100)
      const day = d % 100
      const key = groupLevel === 'month'
        ? `${y}-${String(m).padStart(2, '0')}`
        : `${y}-${String(m).padStart(2, '0')}-${String(day).padStart(2, '0')}`

      if (key !== currentKey && currentKey !== '' && groupLevel !== 'none') {
        flushBatch()
      }
      currentKey = key
      currentLabel = groupLevel === 'month'
        ? `${y}年${m}月`
        : `${y}年${m}月${day}日`
      currentBatch.push(item)
    }
    flushBatch()

    return rows
  }

  const totalPhotos = computed(() => allItems.value.length)

  return {
    allItems,
    loading,
    error,
    earliestYear,
    latestYear,
    totalPhotos,
    init,
    buildRows,
  }
}
