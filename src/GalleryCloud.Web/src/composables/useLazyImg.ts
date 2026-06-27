import type { Directive } from 'vue'

const loading = new Set<string>() // track currently loading URLs
const maxConcurrent = 8

export const vLazyImg: Directive<HTMLImageElement, string> = {
  mounted(el, binding) {
    const src = binding.value
    if (!src) return

    const observer = new IntersectionObserver(
      (entries) => {
        let started = 0
        for (const entry of entries) {
          if (!entry.isIntersecting) continue
          const lazySrc = el.dataset.lazySrc
          if (!lazySrc || el.src === lazySrc) continue
          if (loading.size >= maxConcurrent) break
          if (loading.has(lazySrc)) continue

          loading.add(lazySrc)
          el.onerror = () => { loading.delete(lazySrc); el.style.display = 'none' }
          el.onload = () => { loading.delete(lazySrc) }
          el.src = lazySrc
          started++
          if (started >= 4) break // max per batch
        }
      },
      { rootMargin: '80px' }
    )

    el.dataset.lazySrc = src
    observer.observe(el)
    ;(el as any).__lazyObserver = observer
  },

  updated(el, binding) {
    const src = binding.value
    if (src && src !== el.dataset.lazySrc) {
      el.dataset.lazySrc = src
      el.src = ''
      const obs = (el as any).__lazyObserver as IntersectionObserver | undefined
      if (obs) {
        obs.unobserve(el)
        obs.observe(el)
      }
    }
  },

  unmounted(el) {
    const obs = (el as any).__lazyObserver as IntersectionObserver | undefined
    obs?.disconnect()
    el.src = ''
  },
}
