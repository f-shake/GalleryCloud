<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import client from '../api/client'
import { ElMessage } from 'element-plus'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'

const store = usePhotoViewStore()

const photo = ref<any>(null)
const favorited = ref(false)
const showInfo = ref(false)
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

// Swipe-down to dismiss
const dismissY = ref(0)
const dismissing = ref(false)
let isSwipingDown = false
let swipeStartY = 0

const vw = window.innerWidth
const vh = window.innerHeight

const startTransform = computed(() => {
  const s = store.startRect ?? { x: 0, y: 0, width: 1, height: 1 }
  const sx = s.width / vw
  const sy = s.height / vh
  const scx = s.x + s.width / 2
  const scy = s.y + s.height / 2
  const ecx = vw / 2
  const ecy = vh / 2
  return `translate(${scx - ecx}px, ${scy - ecy}px) scale(${sx}, ${sy})`
})

const imgStyle = computed(() => {
  const base = { width: vw + 'px', height: vh + 'px' }
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
const zoomable = computed(() => phase.value === 'done' || phase.value === 'show')

function clampOffset() {
  if (scale.value <= 1) { offsetX.value = 0; offsetY.value = 0; return }
  const maxX = 0
  const minX = vw * (1 - scale.value)
  const maxY = 0
  const minY = vh * (1 - scale.value)
  offsetX.value = Math.max(minX, Math.min(maxX, offsetX.value))
  offsetY.value = Math.max(minY, Math.min(maxY, offsetY.value))
}

const src = computed(() => previewReady.value ? previewSrc.value : gridSrc.value)
const originalUrl = computed(() => `/api/photos/${store.photoId}/file?token=${localStorage.getItem('token') || ''}`)

watch(() => store.open, async (val) => {
  if (!val || !store.photoId) return
  dismissY.value = 0; dismissing.value = false
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
    setTimeout(() => {
      if (sid !== store.session) return
      showBar.value = true
      phase.value = 'show'
      // Load preview AFTER animation completes
      const previewImg = new Image()
      previewImg.onload = () => {
        if (sid === store.session) { previewSrc.value = previewImg.src; previewReady.value = true; phase.value = 'done' }
      }
      previewImg.onerror = () => { if (sid === store.session) phase.value = 'done' }
      previewImg.src = thumbUrl(id, 'preview', 2560)
    }, 380)
  })

  try { const res = await client.get(`/photos/${id}`); if (sid === store.session) photo.value = res.data } catch { /* */ }
})

function toggleFav() {
  const id = store.photoId!
  favorited.value ? client.delete(`/favorites/${id}`).then(() => favorited.value = false)
    : client.post(`/favorites/${id}`).then(() => favorited.value = true)
}

function doClose() {
  if (phase.value === 'start') return
  dismissY.value = 0; dismissing.value = false
  showBar.value = false; showInfo.value = false
  phase.value = 'exit'
  setTimeout(() => store.close(), 350)
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') doClose()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onUnmounted(() => window.removeEventListener('keydown', onKeydown))

// ── Zoom (mouse wheel) ──────────────────────────────────────
function onWheel(e: WheelEvent) {
  if (!zoomable.value) return
  e.preventDefault()
  const delta = e.deltaY > 0 ? -0.1 : 0.1
  const newScale = Math.max(1, Math.min(8, scale.value + delta))
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

// ── Touch (pinch + drag + swipe-down dismiss) ────────────────
function onTouchStart(e: TouchEvent) {
  // Skip if any touch point is on a button/UI element
  for (let i = 0; i < e.touches.length; i++) {
    const el = document.elementFromPoint(e.touches[i].clientX, e.touches[i].clientY)
    if (el && el.closest('.pv-topbar, .pv-info, .glass-btn')) return
  }
  if (e.touches.length === 2) {
    if (!zoomable.value) return
    lastPinchDist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
  } else if (e.touches.length === 1 && scale.value > 1) {
    if (!zoomable.value) return
    isDragging = true
    dragStartX = e.touches[0].clientX
    dragStartY = e.touches[0].clientY
    startOffsetX = offsetX.value
    startOffsetY = offsetY.value
  } else if (e.touches.length === 1 && scale.value === 1 && (phase.value === 'show' || phase.value === 'done')) {
    // Swipe down to dismiss
    isSwipingDown = true
    swipeStartY = e.touches[0].clientY
    dismissing.value = true
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2) {
    if (!zoomable.value) return
    e.preventDefault()
    const dist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
    if (lastPinchDist > 0) {
      const ratio = dist / lastPinchDist
      const newScale = Math.max(1, Math.min(8, scale.value * ratio))
      const mx = (e.touches[0].clientX + e.touches[1].clientX) / 2
      const my = (e.touches[0].clientY + e.touches[1].clientY) / 2
      offsetX.value = mx - (mx - offsetX.value) * (newScale / scale.value)
      offsetY.value = my - (my - offsetY.value) * (newScale / scale.value)
      scale.value = newScale
      clampOffset()
    }
    lastPinchDist = dist
  } else if (e.touches.length === 1 && isDragging) {
    e.preventDefault()
    offsetX.value = startOffsetX + (e.touches[0].clientX - dragStartX)
    offsetY.value = startOffsetY + (e.touches[0].clientY - dragStartY)
    clampOffset()
  } else if (e.touches.length === 1 && isSwipingDown) {
    const dy = e.touches[0].clientY - swipeStartY
    if (dy > 0) { e.preventDefault(); dismissY.value = dy * 0.8 } // damped
  }
}
function onTouchEnd() {
  isDragging = false; lastPinchDist = 0
  if (isSwipingDown) {
    isSwipingDown = false
    if (dismissY.value > 100) {
      doClose()
    } else {
      dismissY.value = 0
      setTimeout(() => { dismissing.value = false }, 250)
    }
  }
}

// ── Double-click to reset zoom ──────────────────────────────
function onDblClick() {
  scale.value = 1; offsetX.value = 0; offsetY.value = 0; clampOffset()
}

async function downloadOriginal() {
  const id = store.photoId
  if (!id) return
  const token = localStorage.getItem('token') || ''
  try {
    const res = await fetch(`/api/photos/${id}/file`, {
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
function getExt(mime: string): string {
  const map: Record<string, string> = { 'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp', 'image/heic': '.heic', 'image/avif': '.avif' }
  return map[mime] || ''
}
</script>

<template>
  <Teleport to="body">
    <div v-if="store.photoId" :class="['pv-bg', backdropOn ? 'pv-bg--on' : '']" @click="doClose" />

    <!-- Image area with zoom/pan -->
    <div
      v-if="store.photoId"
      class="pv-img-wrap"
      :class="{ 'pv-img-wrap--zoomable': zoomable }"
      @wheel="onWheel"
      @mousedown="onMouseDown"
      @mousemove="onMouseMove"
      @mouseup="onMouseUp"
      @mouseleave="onMouseUp"
      @touchstart.passive="onTouchStart"
      @touchmove="onTouchMove"
      @touchend="onTouchEnd"
      @dblclick="onDblClick"
    >
      <div v-if="!src" class="pv-placeholder" />
      <img v-else :src="src" :class="imgClass" :style="imgStyle" draggable="false" />
    </div>

    <!-- Top bar -->
    <div v-if="showBar" class="pv-topbar">
      <el-button circle :icon="'ArrowLeft'" @click="doClose" class="glass-btn" />
      <el-icon v-if="!previewReady" class="is-loading" :size="20" style="color:var(--el-text-color-secondary);margin-left:4px"><Loading /></el-icon>
      <div style="flex:1" />
      <el-button circle :icon="favorited ? 'StarFilled' : 'Star'" @click="toggleFav"
        :class="['glass-btn', favorited ? 'fav-active' : '']" />
      <el-button circle :icon="'Download'" class="glass-btn" @click="downloadOriginal" />
      <el-button circle :icon="'InfoFilled'" @click="showInfo = !showInfo" class="glass-btn" />
    </div>

    <!-- Info -->
    <Transition name="info-slide">
      <div v-if="showInfo && photo" class="pv-info" @click.stop>
        <h4 style="margin:0 0 12px;font-size:15px">照片信息</h4>
        <div class="info-grid">
          <div><span>文件名</span><b>{{ photo.fileName }}</b></div>
          <div><span>拍摄时间</span><b>{{ photo.takenAt || '未知' }}</b></div>
          <div><span>尺寸</span><b>{{ photo.width }} × {{ photo.height }}</b></div>
          <div><span>格式</span><b>{{ photo.fileFormat }}</b></div>
          <div><span>设备</span><b>{{ photo.deviceModel || '未知' }}</b></div>
          <div><span>文件大小</span><b>{{ (photo.fileSize / 1024 / 1024).toFixed(1) }} MB</b></div>
          <div><span>GPS</span><b>{{ photo.latitude ? `${photo.latitude.toFixed(4)}, ${photo.longitude!.toFixed(4)}` : '无' }}</b></div>
        </div>
      </div>
    </Transition>
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
  position: fixed; inset: 0; z-index: 9999;
  display: flex; align-items: center; justify-content: center;
  overflow: hidden;
  pointer-events: none;
  cursor: grab;
}
.pv-img-wrap--zoomable { pointer-events: auto; }
.pv-img-wrap--zoomable:active { cursor: grabbing; }

.pv-placeholder {
  width: 100%; height: 100%;
  background: var(--el-bg-color);
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
  position: fixed; bottom: 0; left: 0; right: 0; z-index: 10000;
  background: var(--el-bg-color); backdrop-filter: blur(20px);
  padding: 20px 24px; border-radius: 16px 16px 0 0;
  color: var(--el-text-color-primary); max-height: 60vh; overflow-y: auto;
  border-top: 1px solid var(--el-border-color-light);
}
.info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; font-size: 12px; }
.info-grid div { display: flex; flex-direction: column; gap: 2px; }
.info-grid span { color: var(--el-text-color-secondary); font-size: 11px; }
.info-grid b { color: var(--el-text-color-primary); font-size: 13px; }

.info-slide-enter-active { transition: transform .25s ease, opacity .25s ease; }
.info-slide-leave-active { transition: transform .2s ease, opacity .2s ease; }
.info-slide-enter-from,
.info-slide-leave-to { transform: translateY(100%); opacity: 0; }
</style>
