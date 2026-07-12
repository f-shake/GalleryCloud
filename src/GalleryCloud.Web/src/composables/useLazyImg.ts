import type { Directive } from 'vue'
import { useThumbnailQueue } from './useThumbnailQueue'

let queue: ReturnType<typeof useThumbnailQueue> | null = null
function getQueue() {
  if (!queue) queue = useThumbnailQueue()
  return queue
}

// Match internal URL: /api/photos/{id}/thumbnail
const internalRe = /\/api\/photos\/([^/]+)\/thumbnail/

export const vLazyImg: Directive<HTMLImageElement, string> = {
  mounted(el, binding) {
    const src = binding.value
    if (!src) return

    const match = src.match(internalRe)
    const photoId = match?.[1]

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) {
            if (photoId) getQueue().unregister(photoId)
            continue
          }
          observer.disconnect()
          if (photoId) {
            getQueue().register(photoId, el)
          } else {
            el.src = src
          }
        }
      },
      { rootMargin: '80px' }
    )

    if (photoId) el.dataset.lazyId = photoId
    observer.observe(el)
    ;(el as any).__lazyObserver = observer
  },

  unmounted(el) {
    const obs = (el as any).__lazyObserver as IntersectionObserver | undefined
    obs?.disconnect()
    if (el.dataset.lazyId) getQueue().unregister(el.dataset.lazyId)
  },
}
