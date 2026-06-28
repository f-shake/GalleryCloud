import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { PhotoIdItem } from '../types'

export interface ThumbRect { x: number; y: number; width: number; height: number }

let sessionId = 0

export const usePhotoViewStore = defineStore('photoView', () => {
  const photoId = ref<string | null>(null)
  const startRect = ref<ThumbRect | null>(null)
  const startImgSrc = ref<string>('')
  const open = ref(false)
  const session = ref(0)
  const cancelTick = ref(0)
  let closeTimer: ReturnType<typeof setTimeout> | null = null

  // Ordered photo list for prev/next navigation (optional — only timeline provides it)
  const allItems = ref<PhotoIdItem[]>([])

  const currentIndex = computed(() => {
    if (!photoId.value || allItems.value.length === 0) return -1
    return allItems.value.findIndex(item => item.id === photoId.value)
  })

  const hasPrev = computed(() => currentIndex.value > 0)
  const hasNext = computed(() => currentIndex.value >= 0 && currentIndex.value < allItems.value.length - 1)

  function show(id: string, rect: ThumbRect, imgSrc?: string, items?: PhotoIdItem[]) {
    if (open.value) return
    if (closeTimer) { clearTimeout(closeTimer); closeTimer = null }
    sessionId++
    session.value = sessionId
    photoId.value = id
    startRect.value = rect
    startImgSrc.value = imgSrc || ''
    if (items) allItems.value = items
    open.value = true
  }

  function navigatePrev() {
    const idx = currentIndex.value
    if (idx > 0 && allItems.value[idx - 1]) {
      const item = allItems.value[idx - 1]
      photoId.value = item.id
      sessionId++
      session.value = sessionId
      startImgSrc.value = ''
    }
  }

  function navigateNext() {
    const idx = currentIndex.value
    if (idx >= 0 && idx < allItems.value.length - 1 && allItems.value[idx + 1]) {
      const item = allItems.value[idx + 1]
      photoId.value = item.id
      sessionId++
      session.value = sessionId
      startImgSrc.value = ''
    }
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
        allItems.value = []
      }
      closeTimer = null
    }, 350)
  }

  return { photoId, startRect, startImgSrc, open, session, cancelTick, allItems, currentIndex, hasPrev, hasNext, show, close, navigatePrev, navigateNext }
})
