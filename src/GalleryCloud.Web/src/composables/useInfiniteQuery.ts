import { ref } from 'vue'
import client from '../api/client'

export function useInfiniteQuery(url: string, params: Record<string, any> = {}, pageSize = 50) {
  const items = ref<any[]>([])
  const loading = ref(false)
  const page = ref(1)
  const hasMore = ref(true)

  async function loadMore() {
    if (loading.value || !hasMore.value) return
    loading.value = true
    try {
      const res = await client.get(url, { params: { ...params, page: page.value, limit: pageSize } })
      const data = res.data
      items.value.push(...(data.photos || data.items || []))
      hasMore.value = data.hasMore ?? (items.value.length < data.total)
      page.value++
    } catch { /* ignore */ }
    finally { loading.value = false }
  }

  function reset() {
    items.value = []
    page.value = 1
    hasMore.value = true
  }

  return { items, loading, hasMore, loadMore, reset }
}
