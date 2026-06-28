import { ref, watch } from 'vue'
import client from '../api/client'
import type { TimelineGroup, TimelineResponse } from '../types'

export function useTimeline(groupLevel: { value: 'day' | 'month' | 'none' }, columns: { value: number }) {
  const groups = ref<TimelineGroup[]>([])
  const loading = ref(false)
  const nextCursor = ref<string | null>(null)
  const hasMore = ref(true)

  function calcLimit() {
    const vh = window.innerHeight
    const gap = 4
    const col = columns.value
    // Cell width = viewport width / columns, cell is square (aspect-ratio:1), plus gap
    const cellSize = Math.floor(window.innerWidth / col) + gap
    const rowsPerPage = Math.ceil(vh / cellSize) + 2  // +2 rows buffer
    return Math.min(rowsPerPage * col, 500)             // cap at 500
  }

  async function loadMore() {
    if (loading.value || !hasMore.value) return
    loading.value = true
    try {
      const limit = calcLimit()
      const params: any = { groupLevel: groupLevel.value, limit }
      if (nextCursor.value) params.cursor = nextCursor.value

      const res = await client.get<TimelineResponse>('/timeline', { params })
      groups.value.push(...res.data.groups)
      nextCursor.value = res.data.nextCursor
      hasMore.value = res.data.hasMore
    } catch { /* ignore */ }
    finally { loading.value = false }
  }

  function reset() {
    groups.value = []
    nextCursor.value = null
    hasMore.value = true
  }

  // Reload when groupLevel changes
  watch(() => groupLevel.value, () => {
    reset()
    loadMore()
  })

  return { groups, loading, hasMore, loadMore, reset }
}
