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
      allItems.value = res.data as PhotoIdItem[]
      if (allItems.value.length > 0) {
        // Find first/last items with valid takenAt (nulls may be at edges)
        const firstWithDate = allItems.value.find(i => i.takenAt)
        let lastWithDate: PhotoIdItem | undefined
        for (let i = allItems.value.length - 1; i >= 0; i--) {
          if (allItems.value[i].takenAt) { lastWithDate = allItems.value[i]; break }
        }
        latestYear.value = firstWithDate?.takenAt ? new Date(firstWithDate.takenAt).getFullYear() : 0
        earliestYear.value = lastWithDate?.takenAt ? new Date(lastWithDate.takenAt).getFullYear() : 0
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
      if (!item.takenAt) {
        currentBatch.push(item)
        continue
      }
      const d = new Date(item.takenAt)
      const key = groupLevel === 'month'
        ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
        : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

      if (key !== currentKey && currentKey !== '' && groupLevel !== 'none') {
        flushBatch()
      }
      currentKey = key
      currentLabel = groupLevel === 'month'
        ? `${d.getFullYear()}年${d.getMonth() + 1}月`
        : `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
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
