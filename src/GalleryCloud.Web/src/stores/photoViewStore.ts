import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface ThumbRect { x: number; y: number; width: number; height: number }

let sessionId = 0

export const usePhotoViewStore = defineStore('photoView', () => {
  const photoId = ref<string | null>(null)
  const startRect = ref<ThumbRect | null>(null)
  const startImgSrc = ref<string>('')  // blob URL from grid thumbnail
  const open = ref(false)
  const session = ref(0)
  const cancelTick = ref(0)
  let closeTimer: ReturnType<typeof setTimeout> | null = null

  function show(id: string, rect: ThumbRect, imgSrc?: string) {
    if (open.value) return
    if (closeTimer) { clearTimeout(closeTimer); closeTimer = null }
    sessionId++
    session.value = sessionId
    photoId.value = id
    startRect.value = rect
    startImgSrc.value = imgSrc || ''
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
        startImgSrc.value = ''
      }
      closeTimer = null
    }, 350)
  }

  return { photoId, startRect, startImgSrc, open, session, cancelTick, show, close }
})
