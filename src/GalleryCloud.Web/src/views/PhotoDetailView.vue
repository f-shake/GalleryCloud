<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, defineAsyncComponent } from 'vue'
import client from '../api/client'
import { ElMessage } from 'element-plus'
import { thumbUrl, fetchThumbnail } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useAuthStore } from '../stores/authStore'
const MapEmbed = defineAsyncComponent(() => import('../components/MapEmbed.vue'))

const store = usePhotoViewStore()
const auth = useAuthStore()

const photo = ref<any>(null)
const favorited = ref(false)
const showInfo = ref(false)
const layoutMode = ref<'split' | 'full'>('full')
watch(showInfo, (val) => {
  if (val) {
    layoutMode.value = 'split'
  } else {
    setTimeout(() => { layoutMode.value = 'full' }, 350)
  }
})
const phase = ref<'start' | 'expand' | 'show' | 'done' | 'exit'>('start')
const showBar = ref(false)
const gridSrc = ref('')
const previewSrc = ref('')
const previewReady = ref(false)

// Zoom & pan
const scale = ref(1)
const offsetX = ref(0)
const offsetY = ref(0)
const zoomAnimating = ref(false)
let zoomTimer: any = null
let isDragging = false
let dragStartX = 0, dragStartY = 0
let startOffsetX = 0, startOffsetY = 0
let lastPinchDist = 0
let _pinchInitDist = 0, _pinchInitScale = 1, _pinchInitOx = 0, _pinchInitOy = 0, _pinchInitMx = 0, _pinchInitMy = 0

// ── Carousel navigation (only during show/done, zero impact on FLIP) ──
const slideOffset = ref(0)
const slideSnapping = ref(false)
const CAROUSEL_BASE = -1
const gridError = ref(false)

function commitSlide(direction: number) {
  if (slideSnapping.value) return // guard against double-tap
  const target = direction > 0 ? 0 : -2 * vw.value
  slideSnapping.value = true; slideOffset.value = target
  setTimeout(() => {
    if (direction < 0) store.navigateNext(); else store.navigatePrev()
    // Reset carousel instantly after navigation (no animation on reset)
    requestAnimationFrame(() => {
      slideSnapping.value = false
      slideOffset.value = CAROUSEL_BASE * vw.value
    })
  }, 300)
}

const carouselStyle = computed(() => {
  if (phase.value === 'start' || phase.value === 'exit') return {}
  return { transform: `translate(${slideOffset.value}px, ${dismissY.value}px)`, transition: slideSnapping.value ? 'transform .3s ease' : 'none' }
})

// ── Info panel swipe ──
const infoDragY = ref(0); const infoSnapping = ref(false); let _infoTouchStartY = 0; let _touchDragActive = false
function onInfoTouchStart(e: TouchEvent) {
  for (let i = 0; i < e.touches.length; i++) {
    const el = document.elementFromPoint(e.touches[i].clientX, e.touches[i].clientY)
    if (el && el.closest('.map-embed')) { _touchDragActive = false; return }
  }
  _touchDragActive = true
  _infoTouchStartY = e.touches[0].clientY; infoDragY.value = 0; infoSnapping.value = false
}
function onInfoTouchMove(e: TouchEvent) { if (!_touchDragActive) return; const dy = e.touches[0].clientY - _infoTouchStartY; if (dy > 0) { infoDragY.value = dy; if (e.cancelable) e.preventDefault() } }
function onInfoTouchEnd() { if (!_touchDragActive) return; if (infoDragY.value > 60) { showInfo.value = false; infoDragY.value = 0 } else { infoSnapping.value = true; requestAnimationFrame(() => { infoDragY.value = 0 }); setTimeout(() => { infoSnapping.value = false }, 250) } }

// Mouse drag on info panel (desktop)
let _infoMouseStartY = 0; let _mouseDragActive = false
function onInfoMouseDown(e: MouseEvent) {
  if ((e.target as HTMLElement)?.closest?.('.map-embed')) { _mouseDragActive = false; return }
  _mouseDragActive = true
  _infoMouseStartY = e.clientY; infoDragY.value = 0; infoSnapping.value = false; window.addEventListener('mousemove', onInfoMouseMove); window.addEventListener('mouseup', onInfoMouseUp)
}
function onInfoMouseMove(e: MouseEvent) { if (!_mouseDragActive) return; const dy = e.clientY - _infoMouseStartY; if (dy > 0) infoDragY.value = dy }
function onInfoMouseUp() { window.removeEventListener('mousemove', onInfoMouseMove); window.removeEventListener('mouseup', onInfoMouseUp); if (!_mouseDragActive) return; if (infoDragY.value > 60) { showInfo.value = false; infoDragY.value = 0 } else { infoSnapping.value = true; requestAnimationFrame(() => { infoDragY.value = 0 }); setTimeout(() => { infoSnapping.value = false }, 250) } }

// Swan-down to dismiss + swipe nav
const dismissY = ref(0)
const dismissing = ref(false)
let isSwipingDown = false
let swipeStartY = 0
let swipeStartX = 0
let isSwipingHorizontal = false
let swipeHandled = false
let swipeDx = 0

const vw = ref(window.innerWidth)
const vh = ref(window.innerHeight)
const navTop = computed(() => { if (!showInfo.value) return '50%'; return window.innerWidth <= 767 ? '15vh' : '30vh' })

function onResize() {
  const oldVw = vw.value
  vw.value = window.innerWidth
  vh.value = window.innerHeight
  // Scale slideOffset proportionally to keep carousel aligned
  if (oldVw > 0 && !slideSnapping.value) {
    slideOffset.value = (slideOffset.value / oldVw) * vw.value
  }
}

const startTransform = computed(() => {
  const s = store.startRect ?? { x: 0, y: 0, width: 1, height: 1 }
  const sx = s.width / vw.value
  const sy = s.height / vh.value
  const scx = s.x + s.width / 2
  const scy = s.y + s.height / 2
  const ecx = vw.value / 2
  const ecy = vh.value / 2
  return `translate(${scx - ecx}px, ${scy - ecy}px) scale(${sx}, ${sy})`
})

const imgStyle = computed(() => {
  const base = layoutMode.value === 'split'
    ? { width: '100%', height: '100%' }
    : { width: vw.value + 'px', height: vh.value + 'px' }
  if (phase.value === 'start' || phase.value === 'exit')
    return { ...base, transform: startTransform.value }
  const parts: string[] = []
  if (dismissY.value !== 0) parts.push(`translateY(${dismissY.value}px)`)
  if (scale.value !== 1 || offsetX.value !== 0 || offsetY.value !== 0)
    parts.push(`translate(${offsetX.value}px, ${offsetY.value}px) scale(${scale.value})`)
  if (parts.length > 0) return { ...base, transform: parts.join(' ') }
  return base
})

const imgClass = computed(() => {
  const c = ['pv-img']
  if (phase.value !== 'start') c.push('pv-img--anim')
  if (phase.value === 'start') c.push('pv-img--cover')
  if (phase.value === 'exit') c.push('pv-img--fade')
  if (phase.value === 'done' || phase.value === 'show') {
    c.push('pv-img--zoom')
    if (zoomAnimating.value || dismissing.value) c.push('pv-img--zoom-anim')
  }
  return c
})

const backdropOn = computed(() => phase.value !== 'start' && phase.value !== 'exit')
const zoomable = computed(() => (phase.value === 'done' || phase.value === 'show') && !slideSnapping.value)

function clampOffset() {
  if (scale.value <= 1) { offsetX.value = 0; offsetY.value = 0; return }
  const maxX = 0
  const minX = vw.value * (1 - scale.value)
  const maxY = 0
  const minY = vh.value * (1 - scale.value)
  offsetX.value = Math.max(minX, Math.min(maxX, offsetX.value))
  offsetY.value = Math.max(minY, Math.min(maxY, offsetY.value))
}

const src = computed(() => previewReady.value ? previewSrc.value : gridSrc.value)

// 预览打开时推一个历史记录，Android 返回键可关闭
watch(() => store.open, async (val) => {
  if (val) { window.history.pushState({ preview: true }, '') }
  if (!val || !store.photoId) return
  dismissY.value = 0; dismissing.value = false; gridError.value = false
  const id = store.photoId
  const sid = store.session
  scale.value = 1
  offsetX.value = 0
  offsetY.value = 0

  gridSrc.value = store.startImgSrc || thumbUrl(id, 'grid', 400)
  previewReady.value = false
  photo.value = null
  favorited.value = false
  showInfo.value = false
  phase.value = 'start'
  showBar.value = false

  requestAnimationFrame(() => {
    if (sid !== store.session) return
    phase.value = 'expand'
    setTimeout(async () => {  // 改为 async，使用带重试的 fetchThumbnail
      if (sid !== store.session) return
      showBar.value = true
      phase.value = 'show'
      // 使用 fetchThumbnail（自动重试 202），比 new Image() 可靠
      try {
        const blobUrl = await fetchThumbnail(id, 'preview', 2560)
        if (sid === store.session) {
          previewSrc.value = blobUrl; previewReady.value = true; gridError.value = false; phase.value = 'done'
        }
      } catch {
        if (sid === store.session) phase.value = 'done'
      }
    }, 380)
  })

  try { const res = await client.get(`/photos/${id}`); if (sid === store.session) photo.value = res.data } catch { /* */ }
  try { const r = await client.get(`/favorites/check/${id}`); if (sid === store.session) favorited.value = r.data.isFavorited } catch { /* */ }
  // 刷新用户信息，确保 roots 数据用于显示文件路径
  try { const me = await client.get('/auth/me'); if (sid === store.session && auth.user) auth.user.roots = me.data.roots } catch { /* */ }
  slideOffset.value = CAROUSEL_BASE * vw.value
})

// Navigation: photoId changes without close → load new photo immediately
watch(() => store.photoId, async (newId, oldId) => {
  if (!newId || newId === oldId || !store.open) return
  // Only allow nav when photo is fully displayed (show/done), not during FLIP or exit
  if (phase.value !== 'show' && phase.value !== 'done') return
  gridError.value = false; scale.value = 1; offsetX.value = 0; offsetY.value = 0
  dismissY.value = 0; dismissing.value = false
  const sid = store.session
  gridSrc.value = thumbUrl(newId, 'grid', 400)
  previewReady.value = false; favorited.value = false
  phase.value = 'show'; showBar.value = true; slideOffset.value = CAROUSEL_BASE * vw.value
  // 使用带重试的 fetchThumbnail
  try {
    const blobUrl = await fetchThumbnail(newId, 'preview', 2560)
    if (sid === store.session) {
      previewSrc.value = blobUrl; previewReady.value = true; gridError.value = false; phase.value = 'done'
    }
  } catch {
    if (sid === store.session) phase.value = 'done'
  }
  try { const res = await client.get(`/photos/${newId}`); if (sid === store.session) photo.value = res.data } catch { /* */ }
  try { const r = await client.get(`/favorites/check/${newId}`); if (sid === store.session) favorited.value = r.data.isFavorited } catch { /* */ }
  try { const me = await client.get('/auth/me'); if (sid === store.session && auth.user) auth.user.roots = me.data.roots } catch { /* */ }
})

function toggleFav() {
  const id = store.photoId!
  favorited.value ? client.delete(`/favorites/${id}`).then(() => favorited.value = false)
    : client.post(`/favorites/${id}`).then(() => favorited.value = true)
}

function onBackdropClick() {
  if (showInfo.value) {
    showInfo.value = false
  } else {
    doClose()
  }
}

function doClose() {
  if (phase.value === 'start' || slideSnapping.value) return
  dismissY.value = 0; dismissing.value = false; gridError.value = false
  showBar.value = false; showInfo.value = false

  if (scale.value !== 1) {
    // 放大状态：先缩回全屏，再退出到缩略图
    zoomAnimating.value = true  // 启用过渡（pv-img--zoom-anim）
    scale.value = 1; offsetX.value = 0; offsetY.value = 0
    setTimeout(() => {
      zoomAnimating.value = false
      phase.value = 'exit'
      setTimeout(() => store.close(), 350)
    }, 200)
  } else {
    // 未放大：直接退出到缩略图
    scale.value = 1; offsetX.value = 0; offsetY.value = 0
    phase.value = 'exit'
    setTimeout(() => store.close(), 350)
  }
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') { doClose(); return }
  if (e.key === 'ArrowLeft' && store.hasPrev && !slideSnapping.value) { commitSlide(1); return }
  if (e.key === 'ArrowRight' && store.hasNext && !slideSnapping.value) { commitSlide(-1); return }
}

function onPopState() {
  if (store.open) doClose()
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
  window.addEventListener('resize', onResize)
  window.addEventListener('popstate', onPopState)
})
onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown)
  window.removeEventListener('resize', onResize)
  window.removeEventListener('popstate', onPopState)
})

// ── Zoom (mouse wheel) ──────────────────────────────────────
function onWheel(e: WheelEvent) {
  if (!zoomable.value) return
  if (e.cancelable) e.preventDefault()
  const factor = e.deltaY > 0 ? 0.9 : 1.1
  const newScale = Math.max(1, Math.min(16, scale.value * factor))
  const ratio = newScale / scale.value
  zoomAnimating.value = true
  clearTimeout(zoomTimer)
  zoomTimer = setTimeout(() => { zoomAnimating.value = false }, 200)
  // Zoom toward cursor: keep the same image point under the mouse
  const mx = e.clientX
  const my = e.clientY
  offsetX.value = mx - (mx - offsetX.value) * ratio
  offsetY.value = my - (my - offsetY.value) * ratio
  scale.value = newScale
  clampOffset()
}

// ── Mouse drag ──────────────────────────────────────────────
function onMouseDown(e: MouseEvent) {
  if (!zoomable.value || scale.value <= 1) return
  isDragging = true
  dragStartX = e.clientX
  dragStartY = e.clientY
  startOffsetX = offsetX.value
  startOffsetY = offsetY.value
}
function onMouseMove(e: MouseEvent) {
  if (!isDragging) return
  offsetX.value = startOffsetX + (e.clientX - dragStartX)
  offsetY.value = startOffsetY + (e.clientY - dragStartY)
  clampOffset()
}
function onMouseUp() { isDragging = false }

// ── Double-click / double-tap (unified, works on desktop & mobile) ──
let lastClickTime = 0, lastClickX = 0, lastClickY = 0

function onImgClick(e: MouseEvent) {
  if (!zoomable.value || slideSnapping.value) return
  const now = Date.now()
  const dist = Math.hypot(e.clientX - lastClickX, e.clientY - lastClickY)
  if (now - lastClickTime < 300 && dist < 50) {
    if (scale.value > 1) {
      animateZoomTo(1, vw.value / 2, vh.value / 2)
    } else {
      animateZoomTo(2, e.clientX, e.clientY)
    }
    lastClickTime = 0
  } else {
    lastClickTime = now
    lastClickX = e.clientX; lastClickY = e.clientY
  }
}

function animateZoomTo(targetScale: number, cx: number, cy: number) {
  if (targetScale === scale.value && offsetX.value === 0 && offsetY.value === 0) return
  zoomAnimating.value = true
  clearTimeout(zoomTimer)
  const ratio = targetScale / scale.value
  offsetX.value = cx - (cx - offsetX.value) * ratio
  offsetY.value = cy - (cy - offsetY.value) * ratio
  scale.value = targetScale
  clampOffset()
  zoomTimer = setTimeout(() => { zoomAnimating.value = false }, 250)
}

// ── Touch (pinch + drag + swipe-down dismiss) ────────────────
function onTouchStart(e: TouchEvent) {
  // Skip if any touch point is on a button/UI element
  for (let i = 0; i < e.touches.length; i++) {
    const el = document.elementFromPoint(e.touches[i].clientX, e.touches[i].clientY)
    if (el && el.closest('.pv-topbar, .pv-info, .glass-btn')) return
  }
  if (e.touches.length === 2) {
    if (!zoomable.value) return
    dismissing.value = false // kill swipe-dismiss animation
    isSwipingDown = false; clearTimeout(zoomTimer); zoomAnimating.value = false
    lastPinchDist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
    // Anchor values — scale from initial state, not previous frame
    _pinchInitDist = lastPinchDist
    _pinchInitScale = scale.value
    _pinchInitOx = offsetX.value
    _pinchInitOy = offsetY.value
    _pinchInitMx = (e.touches[0].clientX + e.touches[1].clientX) / 2
    _pinchInitMy = (e.touches[0].clientY + e.touches[1].clientY) / 2
  } else if (e.touches.length === 1 && scale.value > 1) {
    if (!zoomable.value) return
    isDragging = true
    dragStartX = e.touches[0].clientX
    dragStartY = e.touches[0].clientY
    startOffsetX = offsetX.value
    startOffsetY = offsetY.value
  } else if (e.touches.length === 1 && scale.value === 1 && (phase.value === 'show' || phase.value === 'done')) {
    isSwipingDown = true
    swipeStartY = e.touches[0].clientY; swipeStartX = e.touches[0].clientX
    isSwipingHorizontal = false; swipeHandled = false
    dismissing.value = true
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2) {
    if (!zoomable.value) return
    if (e.cancelable) e.preventDefault()
    const dist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
    if (_pinchInitDist > 0) {
      const ratio = dist / _pinchInitDist
      const newScale = Math.max(1, Math.min(16, _pinchInitScale * ratio))
      const mx = (e.touches[0].clientX + e.touches[1].clientX) / 2
      const my = (e.touches[0].clientY + e.touches[1].clientY) / 2
      const sRatio = newScale / _pinchInitScale; offsetX.value = mx - (_pinchInitMx - _pinchInitOx) * sRatio
      offsetY.value = my - (_pinchInitMy - _pinchInitOy) * sRatio
      scale.value = newScale
      clampOffset()
    }
    lastPinchDist = dist
  } else if (e.touches.length === 1 && isDragging) {
    if (e.cancelable) e.preventDefault()
    offsetX.value = startOffsetX + (e.touches[0].clientX - dragStartX)
    offsetY.value = startOffsetY + (e.touches[0].clientY - dragStartY)
    clampOffset()
  } else if (e.touches.length === 1 && isSwipingDown) {
    const dx = e.touches[0].clientX - swipeStartX
    const dy = e.touches[0].clientY - swipeStartY
    if (!swipeHandled && Math.abs(dx) > 10 && Math.abs(dx) > Math.abs(dy)) { isSwipingHorizontal = true; swipeHandled = true }
    if (isSwipingHorizontal) { if (e.cancelable) e.preventDefault(); dismissY.value = 0; slideOffset.value = CAROUSEL_BASE * vw.value + dx; swipeDx = dx }
    else if (dy > 0) { if (e.cancelable) e.preventDefault(); dismissY.value = dy * 0.8 }
    else if (dy < -10 && !swipeHandled) { showInfo.value = true; swipeHandled = true }
  }
}
function onTouchEnd() {
  isDragging = false; lastPinchDist = 0
  if (isSwipingDown) {
    isSwipingDown = false
    if (isSwipingHorizontal) {
      dismissing.value = false
      if (Math.abs(swipeDx) > vw.value * 0.15) { commitSlide(swipeDx > 0 ? 1 : -1) }
      else { slideSnapping.value = true; slideOffset.value = CAROUSEL_BASE * vw.value; setTimeout(() => { slideSnapping.value = false }, 300) }
    } else if (dismissY.value > 100) {
      doClose()
    } else {
      dismissY.value = 0
      setTimeout(() => { dismissing.value = false }, 250)
    }
  }
}


async function downloadOriginal() {
  const id = store.photoId
  if (!id) return
  const token = localStorage.getItem('token') || ''
  try {
    const res = await fetch(`${import.meta.env.BASE_URL}api/photos/${id}/file`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (res.status === 404) { ElMessage.error('文件不存在或已被删除'); return }
    if (!res.ok) throw new Error('Download failed')
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = photo.value?.fileName || `${id}${getExt(blob.type)}`
    a.click()
    URL.revokeObjectURL(url)
  } catch { /* */ }
}
function formatDateTime(val: string | null): string {
  if (!val) return ''
  const d = new Date(val)
  if (isNaN(d.getTime())) return val
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

function getExt(mime: string): string {
  const map: Record<string, string> = { 'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp', 'image/heic': '.heic', 'image/avif': '.avif' }
  return map[mime] || ''
}

const displayPath = computed(() => {
  if (!photo.value?.filePath) return ''
  const root = auth.user?.roots?.find(r => r.id === photo.value?.rootId)
  const rootName = root ? root.rootPath.replace(/[/\\]$/, '').split(/[/\\]/).pop() || '' : ''
  return rootName ? rootName + '/' + photo.value.filePath : photo.value.filePath
})

</script>

<template>
  <Teleport to="body">
    <div v-if="store.photoId" :class="['pv-bg', backdropOn ? 'pv-bg--on' : '']" @click="onBackdropClick" />

    <!-- Image area with zoom/pan -->
    <div
      v-if="store.photoId"
      class="pv-img-wrap"
      :class="{ 'pv-img-wrap--zoomable': zoomable, 'pv-img-wrap--shrunk': showInfo }"
      @wheel="onWheel"
      @mousedown="onMouseDown"
      @mousemove="onMouseMove"
      @mouseup="onMouseUp"
      @mouseleave="onMouseUp"
      @touchstart.passive="onTouchStart"
      @touchmove="onTouchMove"
      @touchend="onTouchEnd"
      @click="onImgClick"
    >
      <div v-if="!src || gridError" class="pv-placeholder">
        <el-icon class="is-loading" :size="28"><Loading /></el-icon>
      </div>
      <img v-else :src="src" :class="[imgClass, slideSnapping ? 'pv-img--fade-out' : '']" :style="imgStyle" draggable="false" @error="gridError = true" />
      <!-- Carousel overlay: sibling div, only during show/done for swipe animation -->
      <div v-if="phase === 'show' || phase === 'done'" class="pv-carousel" :style="carouselStyle">
        <div class="pv-carousel-cell" :style="{ left: 0 }"><img v-if="store.hasPrev" :src="thumbUrl(store.allItems[store.currentIndex-1]?.id || '', 'grid', 400)" class="pv-carousel-img" /></div>
        <div class="pv-carousel-cell" :style="{ left: vw + 'px' }" />
        <div class="pv-carousel-cell" :style="{ left: (vw * 2) + 'px' }"><img v-if="store.hasNext" :src="thumbUrl(store.allItems[store.currentIndex+1]?.id || '', 'grid', 400)" class="pv-carousel-img" /></div>
      </div>
    </div>

    <!-- Desktop nav arrows -->
    <div v-if="showBar && store.hasPrev" class="pv-nav pv-nav--prev" :style="{ top: navTop }" @click.stop="commitSlide(1)"><el-icon :size="28"><ArrowLeft /></el-icon></div>
    <div v-if="showBar && store.hasNext" class="pv-nav pv-nav--next" :style="{ top: navTop }" @click.stop="commitSlide(-1)"><el-icon :size="28"><ArrowRight /></el-icon></div>

    <!-- Top bar -->
    <div v-if="showBar" class="pv-topbar">
      <el-button circle :icon="'ArrowLeft'" @click="doClose" class="glass-btn" />
      <el-icon v-if="!previewReady" class="is-loading" :size="20" style="color:var(--el-text-color-secondary);margin-left:4px"><Loading /></el-icon>
      <span v-if="photo?.fileName" class="pv-filename" style="flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px;margin:0 8px;color:#fff;text-shadow:0 0 4px rgba(0,0,0,.8),0 0 2px rgba(0,0,0,.6)">{{ photo.fileName }}</span>
      <span v-if="store.hasPrev || store.hasNext" style="font-size:12px;color:var(--el-text-color-secondary);flex-shrink:0">{{ store.currentIndex + 1 }} / {{ store.allItems.length }}</span>
      <el-button circle :icon="favorited ? 'StarFilled' : 'Star'" @click="toggleFav" :class="['glass-btn', favorited ? 'fav-active' : '']" />
      <el-button circle :icon="'Download'" class="glass-btn" @click="downloadOriginal" />
      <el-button circle :icon="'InfoFilled'" @click="showInfo = !showInfo" class="glass-btn" />
    </div>

    <!-- Info panel — full height with map embed -->
    <Transition name="info-slide">
      <div v-if="showInfo && photo" class="pv-info" :class="{ 'pv-info--snapping': infoSnapping }" @click.stop
        @touchstart.passive="onInfoTouchStart" @touchmove="onInfoTouchMove" @touchend="onInfoTouchEnd"
        @mousedown="onInfoMouseDown"
        :style="infoDragY > 0 ? { transform: `translateY(${infoDragY}px)` } : undefined">
        <div class="pv-info-handle" @click.stop="showInfo = false" />
        <div class="pv-info-body">
          <div class="info-split">
            <div class="info-left">
              <!-- Section 1: 文件路径 -->
              <div class="info-section" style="flex-shrink:0">
                <div class="info-row"><span>文件路径</span><b style="word-break:break-all;font-size:12px">{{ displayPath }}</b></div>
              </div>
              <!-- Section 2: 元数据 -->
              <div class="info-section" style="flex-shrink:0">
                <div class="info-section-title">元数据</div>
                <div class="meta-grid">
                  <div class="meta-cell">
                    <span class="meta-label">时间</span>
                    <span class="meta-value">{{ formatDateTime(photo.takenAt) || '未知' }}</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">设备</span>
                    <span class="meta-value">{{ photo.deviceModel || '未知' }}</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">尺寸</span>
                    <span class="meta-value">{{ photo.width }} × {{ photo.height }}</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">大小</span>
                    <span class="meta-value">{{ (photo.fileSize / 1024 / 1024).toFixed(1) }} MB</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">光圈</span>
                    <span class="meta-value">{{ photo.aperture || '未知' }}</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">焦距</span>
                    <span class="meta-value">
                      <template v-if="photo.focalLength">{{ photo.focalLength }} </template>
                      <template v-if="photo.focalLength35mm != null">（等效{{ photo.focalLength35mm }}mm）</template>
                      <template v-if="!photo.focalLength && photo.focalLength35mm == null">未知</template>
                    </span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">曝光时间</span>
                    <span class="meta-value">{{ photo.exposureTime ? photo.exposureTime + 's' : '未知' }}</span>
                  </div>
                  <div class="meta-cell">
                    <span class="meta-label">ISO</span>
                    <span class="meta-value">{{ photo.iso != null ? photo.iso : '未知' }}</span>
                  </div>
                </div>
              </div>
            </div>
            <div class="info-right">
              <!-- Section 3: 位置 -->
              <div class="info-section" style="display:flex;flex-direction:column;min-height:0;flex:1">
                <div class="info-section-title">位置</div>
                <div v-if="photo.latitude" style="flex-shrink:0;font-size:12px;color:var(--el-text-color-primary);margin-bottom:6px">
                  {{ Number(photo.latitude).toFixed(6) }}, {{ Number(photo.longitude).toFixed(6) }}
                </div>
                <div v-else style="flex-shrink:0;font-size:12px;color:var(--el-text-color-secondary);margin-bottom:6px">无空间信息</div>
                <MapEmbed :latitude="photo.latitude" :longitude="photo.longitude" style="flex:1;min-height:0;border-radius:8px;overflow:hidden" />
              </div>
            </div>
          </div>
        </div>
      </div></Transition>
  </Teleport>
</template>

<style scoped>
.pv-bg {
  position: fixed; inset: 0; z-index: 9998;
  background: transparent;
  transition: background .35s ease;
  pointer-events: none;
}
.pv-bg--on { background: var(--el-bg-color); pointer-events: auto; }

.pv-img-wrap {
  position: fixed; top: 0; right: 0; left: 0; z-index: 9999;
  height: 100%;
  display: flex; align-items: center; justify-content: center;
  overflow: hidden;
  pointer-events: none;
  cursor: grab;
  transition: height .3s ease;
}
.pv-img-wrap--zoomable { pointer-events: auto; touch-action: manipulation; }
.pv-img-wrap--zoomable:active { cursor: grabbing; }
.pv-img-wrap--shrunk { height: 60%; overflow: hidden; }

.pv-placeholder {
  width: 100%; height: 100%;
  background: var(--el-bg-color);
  display: flex; align-items: center; justify-content: center;
}
.pv-img {
  object-fit: contain; border-radius: 0; will-change: transform;
  user-select: none; -webkit-user-select: none;
}
.pv-img:not(.pv-img--anim) { transition: none; border-radius: 4px; }
.pv-img--anim {
  transition: transform .35s cubic-bezier(.25,.46,.45,.94), border-radius .35s ease, opacity .2s ease;
  border-radius: 0;
}
.pv-img--cover { object-fit: cover; }
.pv-img--zoom { transform-origin: 0 0; transition: none !important; }
.pv-img--zoom-anim { transition: transform .2s ease !important; }
.pv-img--fade {
  opacity: 0;
  transition: transform .35s cubic-bezier(.25,.46,.45,.94), border-radius .35s ease, opacity .15s ease .25s;
}
.pv-img--fade-out { opacity: 0 !important; transition: opacity .2s ease !important; }

.pv-topbar {
  position: fixed; top: 0; left: 0; right: 0; z-index: 10000;
  display: flex; align-items: center; gap: 8px;
  padding: 12px 16px; pointer-events: none;
}
.pv-topbar > * { pointer-events: auto; }
.glass-btn {
  background: var(--el-fill-color-light) !important; border: none !important;
  color: var(--el-text-color-primary) !important; backdrop-filter: blur(8px);
}
.glass-btn:hover { background: var(--el-fill-color) !important; }
.fav-active { color: #f59e0b !important; background: rgba(245,158,11,.25) !important; }

.pv-info {
  position: fixed; left: 0; right: 0; bottom: 0; height: 40%; z-index: 10000;
  background: var(--el-bg-color);
  display: flex; flex-direction: column;
  padding: 12px 0 0;
  color: var(--el-text-color-primary);
  overflow: hidden;
  border-radius: 16px 16px 0 0;
  box-shadow: 0 -4px 20px rgba(0,0,0,.1);
}
.pv-info--snapping { transition: transform .25s ease; }
.pv-info-body {
  flex: 1;
  display: flex; flex-direction: column;
  padding: 0 20px 20px;
  min-height: 0;
}
.info-grid {
  display: grid; grid-template-columns: 1fr 1fr; gap: 8px; font-size: 12px;
  flex-shrink: 0;
}
.info-grid div { display: flex; flex-direction: column; gap: 2px; }
.info-grid span { color: var(--el-text-color-secondary); font-size: 11px; }
.info-grid b { color: var(--el-text-color-primary); font-size: 13px; }


.pv-info { padding-top: 12px !important; }
.pv-info-handle { width: 36px; height: 5px; border-radius: 3px; background: var(--el-border-color); margin: 0 auto 8px; flex-shrink: 0; }

.info-section { margin-bottom: 8px; }
.info-split { display: flex; flex: 1; min-height: 0; gap: 12px; }
.info-left { display: flex; flex-direction: column; gap: 8px; width: 50%; overflow-y: auto; }
.info-right { display: flex; flex-direction: column; width: 50%; min-height: 0; }
@media (max-width: 767px) { .pv-info { height: 70%; } .pv-img-wrap--shrunk { height: 30%; } .info-split { flex-direction: column; } .info-left, .info-right { width: 100%; } .info-right { flex: 1; min-height: 200px; } .pv-info-body { padding-bottom: 8px; } }
.info-section-title { font-size: 13px; font-weight: 600; color: var(--el-text-color-secondary); margin-bottom: 8px; }
.info-row { display: flex; flex-direction: column; gap: 2px; font-size: 12px; }
.info-row span { color: var(--el-text-color-secondary); font-size: 11px; }
.info-row b { color: var(--el-text-color-primary); font-size: 13px; }
.meta-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 4px 12px; }
.meta-cell { display: flex; flex-direction: column; gap: 1px; }
.meta-label { display: none; font-size: 11px; color: var(--el-text-color-secondary); white-space: nowrap; }
.meta-value { font-size: 13px; color: var(--el-text-color-primary); }
@media (min-width: 768px) { .meta-label { display: inline; } }

.info-slide-enter-active { transition: transform .25s ease; }
.info-slide-leave-active { transition: transform .2s ease; }
.info-slide-enter-from,
.info-slide-leave-to { transform: translateY(100%); }

/* Desktop nav arrows */
.pv-nav {
  position: fixed; top: 50%; transform: translateY(-50%);
  z-index: 10000; width: 48px; height: 64px;
  display: flex; align-items: center; justify-content: center;
  cursor: pointer; color: var(--el-text-color-primary);
  background: var(--el-fill-color-light); backdrop-filter: blur(8px);
  border-radius: 8px; opacity: .6; transition: opacity .2s ease;
}
.pv-nav:hover { opacity: 1; background: var(--el-fill-color); }
.pv-nav--prev { left: 12px; }
.pv-nav--next { right: 12px; }
@media (max-width: 767px) { .pv-nav { display: none; } }
.pv-carousel { position: absolute; top: 0; left: 0; width: calc(300vw); height: 100%; will-change: transform; pointer-events: none; }
.pv-carousel-cell { position: absolute; top: 0; width: 100vw; height: 100%; display: flex; align-items: center; justify-content: center; }
.pv-carousel-img { width: 100%; height: 100%; object-fit: contain; user-select: none; -webkit-user-select: none; }
</style>
