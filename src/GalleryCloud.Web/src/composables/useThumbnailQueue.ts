import { ref } from 'vue'
import client, { API_BASE } from '../api/client'

interface PendingItem {
  el: HTMLImageElement
  status: 'pending' | 'loading' | 'done'
}

const pending = ref(new Map<string, PendingItem>())
const enqueued = new Set<string>()  // 已入队过的 ID，避免重复入队
let debounceTimer: any = null
let checkTimer: any = null

// Track fetch abort controllers per photo ID
const controllers = new Map<string, AbortController>()

// Persistent blob URL cache — survives register/unregister cycles
// so thumbnails don't need to be re-fetched when scrolling back into view
const blobCache = new Map<string, string>()

export function useThumbnailQueue() {
  function register(id: string, el: HTMLImageElement) {
    // If already cached, apply immediately — no re-fetch needed
    const cached = blobCache.get(id)
    if (cached) {
      el.src = cached
      return
    }
    if (pending.value.has(id)) return
    pending.value.set(id, { el, status: 'pending' })
    debounceCheck()
  }

  function unregister(id: string) {
    pending.value.delete(id)
    enqueued.delete(id)
    // Abort any in-flight fetch for this photo
    const ctrl = controllers.get(id)
    if (ctrl) { ctrl.abort(); controllers.delete(id) }
  }

  function debounceCheck() {
    if (debounceTimer) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(checkNow, 300)
  }

  async function checkNow() {
    if (pending.value.size === 0) return
    if (checkTimer) { clearTimeout(checkTimer); checkTimer = null }

    const ids = [...pending.value.keys()]

    try {
      // 1. Enqueue only new IDs (skip already-enqueued to avoid queue inflation)
      const newIds = ids.filter(id => !enqueued.has(id))
      if (newIds.length > 0) {
        await client.post('/thumbnails/enqueue', { ids: newIds, size: 'grid', width: 400 })
        newIds.forEach(id => enqueued.add(id))
      }

      // 2. Check which are ready
      const res = await client.post('/thumbnails/ready', { ids, size: 'grid', width: 400 })
      const { ready, pending: stillPending } = res.data as { ready: string[], pending: string[] }

      // 3. Load ready thumbnails
      const token = localStorage.getItem('token') || ''
      for (const id of ready) {
        const item = pending.value.get(id)
        if (!item || item.status !== 'pending') continue
        item.status = 'loading'

        const ctrl = new AbortController()
        controllers.set(id, ctrl)

        fetchThumbnailImage(id, token, ctrl.signal).then(blobUrl => {
          blobCache.set(id, blobUrl)
          if (pending.value.has(id)) {
            const el = pending.value.get(id)!.el
            el.src = blobUrl
            pending.value.get(id)!.status = 'done'
          }
        }).catch(() => {
          pending.value.delete(id)
        }).finally(() => {
          controllers.delete(id)
        })
      }

      // 4. Schedule next check if still pending
      if (stillPending.length > 0) {
        checkTimer = setTimeout(checkNow, 2000)
      }
    } catch {
      checkTimer = setTimeout(checkNow, 2000)
    }
  }

  return { register, unregister }
}

async function fetchThumbnailImage(photoId: string, token: string, signal: AbortSignal): Promise<string> {
  const url = `${API_BASE}/photos/${photoId}/thumbnail?size=grid&w=400`

  for (let attempt = 0; attempt < 120; attempt++) {
    if (signal.aborted) throw new Error('aborted')
    try {
      const res = await fetch(url, {
        headers: { Authorization: `Bearer ${token}` },
        signal
      })
      if (res.ok) {
        const blob = await res.blob()
        return URL.createObjectURL(blob)
      }
      if (res.status === 202) {
        await new Promise(r => setTimeout(r, 1200))
        continue
      }
      throw new Error(`status ${res.status}`)
    } catch (e: any) {
      if (e.name === 'AbortError') throw e
      await new Promise(r => setTimeout(r, 1200))
    }
  }
  throw new Error('timeout')
}
