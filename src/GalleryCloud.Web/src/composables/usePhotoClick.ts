import { usePhotoViewStore, toDateInt } from '../stores/photoViewStore'
import type { PhotoIdItem } from '../types'

/**
 * Returns a photo-click handler that computes the bounding rect from the click
 * event and opens the photo viewer with a navigation list.
 *
 * @param getItems — function returning the ordered photo list for prev/next navigation
 */
export function usePhotoClick(getItems: () => PhotoIdItem[]) {
  const viewStore = usePhotoViewStore()

  function onPhotoClick(id: string, e: MouseEvent) {
    const el = e.currentTarget as HTMLElement
    const img = el.querySelector('img')
    const rect = img
      ? { x: img.getBoundingClientRect().left, y: img.getBoundingClientRect().top, width: img.width, height: img.height }
      : { x: el.offsetLeft, y: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight }
    viewStore.show(id, rect, img?.src, getItems())
  }

  return { onPhotoClick }
}

/** Shorthand to build a PhotoIdItem[] from raw photo objects */
export function toNavItems(photos: { id: string; takenAt?: string | null }[]): PhotoIdItem[] {
  return photos.map(p => ({ id: p.id, takenAtDate: toDateInt(p.takenAt) }))
}
