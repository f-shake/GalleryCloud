import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface ThumbRect { x: number; y: number; width: number; height: number }

let sessionId = 0

export const usePhotoViewStore = defineStore('photoView', () => {
  const photoId = ref<string | null>(null)
  const startRect = ref<ThumbRect | null>(null)
  const open = ref(false)
  const session = ref(0)
  // Incremented to signal v-lazy-img to cancel all pending loads
  const cancelTick = ref(0)
  let closeTimer: ReturnType<typeof setTimeout> | null = null

  function show(id: string, rect: ThumbRect) {
    if (open.value) return
    if (closeTimer) { clearTimeout(closeTimer); closeTimer = null }
    sessionId++
    session.value = sessionId
    photoId.value = id
    startRect.value = rect
    cancelTick.value++
    open.value = true
  }

  function close() {
    if (!open.value) return
    open.value = false
    if (closeTimer) clearTimeout(closeTimer)
    const sid = session.value
    closeTimer = setTimeout(() => {
      if (session.value === sid) {
        photoId.value = null
        startRect.value = null
        cancelTick.value++
      }
      closeTimer = null
    }, 350)
  }

  return { photoId, startRect, open, session, cancelTick, show, close }
})
