import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import client from '../api/client'

export type DatePreset = 'today' | 'month' | 'year' | 'all'

function globalKeyHandler(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    const store = useSelectionStore()
    if (store.enabled) {
      store.disable()
    }
  }
}

export function initGlobalSelectionHandler() {
  if (typeof window !== 'undefined') {
    window.addEventListener('keydown', globalKeyHandler)
  }
}

export function disposeGlobalSelectionHandler() {
  if (typeof window !== 'undefined') {
    window.removeEventListener('keydown', globalKeyHandler)
  }
}

export const useSelectionStore = defineStore('selection', () => {
  const enabled = ref(false)
  const selectedIds = ref<Set<string>>(new Set())
  const selectionOrigin = ref<string | null>(null)

  // The current view's full photo list (used for selectToday/Month/Year range calculation)
  const viewPhotos = ref<{ id: string; takenAt: string | null }[]>([])

  // 服务端全选/范围选择时的加载状态
  const bulkLoading = ref(false)

  function enable(source?: string) {
    enabled.value = true
    selectionOrigin.value = source || null
  }

  function disable() {
    enabled.value = false
    selectedIds.value = new Set()
    selectionOrigin.value = null
    viewPhotos.value = []
  }

  function toggle(id: string) {
    const s = new Set(selectedIds.value)
    if (s.has(id)) s.delete(id)
    else s.add(id)
    selectedIds.value = s
  }

  function selectAll(ids?: string[]) {
    if (ids) {
      selectedIds.value = new Set(ids)
    } else {
      selectedIds.value = new Set(viewPhotos.value.map(p => p.id))
    }
  }

  /** 服务端全选（解决 TimelineView lazy-load 下 viewPhotos 不全的问题） */
  async function selectAllFromServer() {
    bulkLoading.value = true
    try {
      const res = await client.get('/search', { params: { limit: 1000000 } })
      const serverPhotos = (res.data.photos as any[]).map(p => ({
        id: p.id as string,
        takenAt: (p.takenAt as string) || null,
      }))
      // 合并到 viewPhotos（累积，不丢失已有的）
      const existingIds = new Set(viewPhotos.value.map(p => p.id))
      const newItems = serverPhotos.filter(p => !existingIds.has(p.id))
      if (newItems.length > 0) {
        viewPhotos.value = [...viewPhotos.value, ...newItems]
      }
      selectedIds.value = new Set(serverPhotos.map(p => p.id))
    } catch {
      // 服务端失败时不改变已选状态
      return
    } finally {
      bulkLoading.value = false
    }
  }

  function clearSelection() {
    selectedIds.value = new Set()
  }

  function setViewPhotos(photos: { id: string; takenAt: string | null }[]) {
    viewPhotos.value = photos
  }

  /** 选择该日期范围内的所有照片（从 viewPhotos 或服务端搜索 API 获取） */
  async function selectByDatePreset(preset: DatePreset) {
    const now = new Date()
    const todayStr = now.toISOString().substring(0, 10)

    // Compute date range boundaries
    let from: string | null = null
    let to: string | null = null

    switch (preset) {
      case 'today': {
        from = todayStr
        to = todayStr
        break
      }
      case 'month': {
        const y = now.getFullYear()
        const m = String(now.getMonth() + 1).padStart(2, '0')
        from = `${y}-${m}-01`
        const lastDay = new Date(y, now.getMonth() + 1, 0).getDate()
        to = `${y}-${m}-${String(lastDay).padStart(2, '0')}`
        break
      }
      case 'year': {
        from = `${now.getFullYear()}-01-01`
        to = `${now.getFullYear()}-12-31`
        break
      }
      case 'all': {
        from = null
        to = null
        break
      }
    }

    // "全部"：从服务端抓取所有照片
    if (!from) {
      bulkLoading.value = true
      try {
        const res = await client.get('/search', { params: { limit: 1000000 } })
        const serverPhotos = (res.data.photos as any[]).map(p => ({
          id: p.id as string,
          takenAt: (p.takenAt as string) || null,
        }))
        viewPhotos.value = serverPhotos
        selectedIds.value = new Set(serverPhotos.map(p => p.id))
      } catch {
        // 服务端失败时回退到本地
        selectedIds.value = new Set(viewPhotos.value.map(p => p.id))
      } finally {
        bulkLoading.value = false
      }
      return
    }

    // "今天"：本地数据通常完整
    if (preset === 'today') {
      const ids = viewPhotos.value
        .filter(p => p.takenAt?.substring(0, 10) === todayStr)
        .map(p => p.id)
      selectedIds.value = new Set(ids)
      return
    }

    // 月/年/全部：从服务端搜索 API 获取该范围的所有照片 ID
    bulkLoading.value = true
    try {
      const params: Record<string, string | number> = { limit: 1000000 }
      if (from) params.from = from
      if (to) params.to = to
      const res = await client.get('/search', { params })
      const serverPhotos = (res.data.photos as any[]).map(p => ({
        id: p.id as string,
        takenAt: (p.takenAt as string) || null,
      }))

      // 合并到 viewPhotos 中（累积，不丢失已有的）
      const existingIds = new Set(viewPhotos.value.map(p => p.id))
      const merged = [...viewPhotos.value]
      for (const p of serverPhotos) {
        if (!existingIds.has(p.id)) {
          merged.push(p)
          existingIds.add(p.id)
        }
      }
      viewPhotos.value = merged

      selectedIds.value = new Set(serverPhotos.map(p => p.id))
    } catch {
      // 服务端失败时回退到本地数据
      const ids = viewPhotos.value
        .filter(p => {
          if (!p.takenAt) return false
          const date = p.takenAt.substring(0, 10)
          return date >= from! && date <= to!
        })
        .map(p => p.id)
      selectedIds.value = new Set(ids)
    } finally {
      bulkLoading.value = false
    }
  }

  const count = computed(() => selectedIds.value.size)

  return {
    enabled, selectedIds, selectionOrigin, viewPhotos, bulkLoading, count,
    enable, disable, toggle, selectAll, selectAllFromServer, clearSelection,
    setViewPhotos, selectByDatePreset,
  }
})
