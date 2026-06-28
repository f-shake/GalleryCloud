import type { Directive } from 'vue'
import { useThumbnailQueue } from './useThumbnailQueue'

let queue: ReturnType<typeof useThumbnailQueue> | null = null
function getQueue() {
  if (!queue) queue = useThumbnailQueue()
  return queue
}

export const vLazyImg: Directive<HTMLImageElement, string> = {
  mounted(el, binding) {
    const src = binding.value
    if (!src) return

    // Parse photoId from URL: /api/photos/{id}/thumbnail?...
    const match = src.match(/\/api\/photos\/([^/]+)\/thumbnail/)
    if (!match) { el.src = src; return }
    const photoId = match[1]

    const observer = new IntersectionObserver(
      (entries) => {
        const q = getQueue()
        for (const entry of entries) {
          if (entry.isIntersecting) {
            q.register(photoId, el)
          } else {
            q.unregister(photoId)
          }
        }
      },
      { rootMargin: '80px' }
    )

    el.dataset.lazyId = photoId
    observer.observe(el)
    ;(el as any).__lazyObserver = observer
  },

  unmounted(el) {
    const obs = (el as any).__lazyObserver as IntersectionObserver | undefined
    obs?.disconnect()
    if (el.dataset.lazyId) getQueue().unregister(el.dataset.lazyId)
  },
}
