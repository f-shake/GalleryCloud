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

  // 服务端范围选择时的加载状态
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

  function clearSelection() {
    selectedIds.value = new Set()
  }

  function setViewPhotos(photos: { id: string; takenAt: string | null }[]) {
    viewPhotos.value = photos
  }

  /**
   * 切换某个日期范围内的全部照片。
   * @param localOnly true = 只用 viewPhotos（数据完整的视图，如文件夹/收藏/搜索），
   *                   false = 先调服务端 /search 再回退 viewPhotos（懒加载视图如 timeline）
   */
  async function selectByDatePreset(preset: DatePreset, localOnly = true) {
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

    /** 获取目标 ID 集合后执行 toggle：已全选则取消，否则全选 */
    function applyToggle(targetIds: Set<string>) {
      if (targetIds.size === 0) return
      const cur = selectedIds.value
      const allSelected = [...targetIds].every(id => cur.has(id))
      const next = new Set(cur)
      if (allSelected) {
        for (const id of targetIds) next.delete(id)
      } else {
        for (const id of targetIds) next.add(id)
      }
      selectedIds.value = next
    }

    // localOnly 模式：纯本地 viewPhotos
    if (localOnly) {
      // 全部
      if (!from) {
        applyToggle(new Set(viewPhotos.value.map(p => p.id)))
        return
      }
      // 今天/本月/今年
      const ids = viewPhotos.value
        .filter(p => {
          if (!p.takenAt) return false
          const date = p.takenAt.substring(0, 10)
          return date >= from! && date <= to!
        })
        .map(p => p.id)
      applyToggle(new Set(ids))
      return
    }

    // 服务端模式：调 /search 获取全量数据，失败时回退到 viewPhotos
    bulkLoading.value = true
    try {
      const params: Record<string, string | number> = { limit: 1000000 }
      if (from) params.from = from
      if (to) params.to = to
      const res = await client.get('/search', { params })
      const serverIds = new Set((res.data.photos as any[]).map(p => p.id as string))
      applyToggle(serverIds)
    } catch {
      // 服务端失败时回退到本地数据
      // "全部" 本地无范围，直接全选 viewPhotos
      if (!from) {
        applyToggle(new Set(viewPhotos.value.map(p => p.id)))
        return
      }
      const ids = viewPhotos.value
        .filter(p => {
          if (!p.takenAt) return false
          const date = p.takenAt.substring(0, 10)
          return date >= from! && date <= to!
        })
        .map(p => p.id)
      applyToggle(new Set(ids))
    } finally {
      bulkLoading.value = false
    }
  }

  const count = computed(() => selectedIds.value.size)

  return {
    enabled, selectedIds, selectionOrigin, viewPhotos, bulkLoading, count,
    enable, disable, toggle, selectAll, clearSelection,
    setViewPhotos, selectByDatePreset,
  }
})
