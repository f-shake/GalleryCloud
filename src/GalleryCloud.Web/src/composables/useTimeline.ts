import { ref, watch } from 'vue'
import client from '../api/client'
import type { TimelineGroup, TimelineResponse } from '../types'

export function useTimeline(groupLevel: { value: 'day' | 'month' | 'none' }) {
  const groups = ref<TimelineGroup[]>([])
  const loading = ref(false)
  const nextCursor = ref<string | null>(null)
  const hasMore = ref(true)

  async function loadMore() {
    if (loading.value || !hasMore.value) return
    loading.value = true
    try {
      const params: any = { groupLevel: groupLevel.value, limit: 30 }
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
