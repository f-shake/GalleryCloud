import { defineStore } from 'pinia'
import { ref, shallowRef } from 'vue'

export interface ThumbRect { x: number; y: number; width: number; height: number }

export const usePhotoViewStore = defineStore('photoView', () => {
  const photoId = ref<string | null>(null)
  const startRect = ref<ThumbRect | null>(null)
  const open = ref(false)

  function show(id: string, rect: ThumbRect) {
    photoId.value = id
    startRect.value = rect
    open.value = true
    history.pushState({ photoView: true }, '', `/photo/${id}`)
  }

  function close() {
    open.value = false
    setTimeout(() => { photoId.value = null; startRect.value = null }, 350)
    history.back()
  }

  // Handle browser back button
  function onPopState() {
    if (open.value) {
      open.value = false
      setTimeout(() => { photoId.value = null; startRect.value = null }, 350)
    }
  }

  return { photoId, startRect, open, show, close, onPopState }
})
