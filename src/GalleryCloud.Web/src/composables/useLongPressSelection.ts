import { useSelectionStore } from '../stores/selectionStore'

export function useLongPressSelection() {
  const store = useSelectionStore()
  let longPressTimer: ReturnType<typeof setTimeout> | null = null

  function onTouchStart(_e: TouchEvent, photoId: string) {
    if (store.enabled) return // already in selection mode, normal tap toggle
    longPressTimer = setTimeout(() => {
      store.enable()
      store.toggle(photoId)
    }, 500)
  }

  function onTouchMove() {
    if (longPressTimer) {
      clearTimeout(longPressTimer)
      longPressTimer = null
    }
  }

  function onTouchEnd() {
    if (longPressTimer) {
      clearTimeout(longPressTimer)
      longPressTimer = null
    }
  }

  return { onTouchStart, onTouchMove, onTouchEnd }
}
