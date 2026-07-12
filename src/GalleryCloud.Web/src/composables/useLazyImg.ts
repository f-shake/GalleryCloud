import type { Directive } from 'vue'
import { useThumbnailQueue } from './useThumbnailQueue'

let queue: ReturnType<typeof useThumbnailQueue> | null = null
function getQueue() {
  if (!queue) queue = useThumbnailQueue()
  return queue
}

// Match internal URL: /api/photos/{id}/thumbnail
const internalRe = /\/api\/photos\/([^/]+)\/thumbnail/
// Match public share URL: /api/public/shares/{token}/photos/{id}/thumbnail
const shareRe = /\/api\/public\/shares\/([^/]+)\/photos\/([^/]+)\/thumbnail/

export const vLazyImg: Directive<HTMLImageElement, string> = {
  mounted(el, binding) {
    const src = binding.value
    if (!src) return

    const internalMatch = src.match(internalRe)
    const shareMatch = src.match(shareRe)
    const photoId = internalMatch?.[1] ?? shareMatch?.[2]

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) {
            if (photoId) getQueue().unregister(photoId)
            continue
          }
          observer.disconnect()
          if (internalMatch) {
            getQueue().register(photoId!, el)
          } else if (shareMatch) {
            loadShareThumbnail(el, src)
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

async function loadShareThumbnail(el: HTMLImageElement, url: string) {
  for (let attempt = 0; attempt < 120; attempt++) {
    try {
      const res = await fetch(url)
      if (res.ok) {
        const blob = await res.blob()
        el.src = URL.createObjectURL(blob)
        return
      }
      if (res.status === 202) {
        await new Promise(r => setTimeout(r, 1200))
        continue
      }
      // Real error — wait and retry
      await new Promise(r => setTimeout(r, 3000))
    } catch {
      await new Promise(r => setTimeout(r, 1200))
    }
  }
  // Never set el.src on failure — CSS background shows instead of broken icon
}
