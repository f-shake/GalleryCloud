<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import client from '../api/client'
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
  return base
})

const imgClass = computed(() => {
  const c = ['pv-img']
  if (phase.value !== 'start') c.push('pv-img--anim')
  if (phase.value === 'start') c.push('pv-img--cover')
  if (phase.value === 'exit') c.push('pv-img--fade')
  return c
})

const backdropOn = computed(() => phase.value !== 'start' && phase.value !== 'exit')

const src = computed(() => previewReady.value ? previewSrc.value : gridSrc.value)
const originalUrl = computed(() => `/api/photos/${store.photoId}/file?token=${localStorage.getItem('token') || ''}`)

watch(() => store.open, async (val) => {
  if (!val || !store.photoId) return
  const id = store.photoId
  const sid = store.session
  // Use the grid's thumbnail (blob URL or API URL) directly
  gridSrc.value = store.startImgSrc || thumbUrl(id, 'grid', 400)

  // Preload preview image (backend generates synchronously for preview)
  const previewImg = new Image()
  previewImg.onload = () => {
    if (sid === store.session) { previewSrc.value = previewImg.src; previewReady.value = true; phase.value = 'done' }
  }
  previewImg.onerror = () => { if (sid === store.session) phase.value = 'done' }
  previewImg.src = thumbUrl(id, 'preview', 2560)
  previewReady.value = false
  photo.value = null
  favorited.value = false
  showInfo.value = false
  phase.value = 'start'
  showBar.value = false

  // One frame to paint start position, then expand
  requestAnimationFrame(() => {
    if (sid !== store.session) return
    phase.value = 'expand'
    // Show buttons after animation completes (350ms)
    setTimeout(() => { if (sid === store.session) { showBar.value = true; phase.value = 'show' } }, 380)
  })

  // Load metadata in background
  try { const res = await client.get(`/photos/${id}`); if (sid === store.session) photo.value = res.data } catch { /* */ }
})

function toggleFav() {
  const id = store.photoId!
  favorited.value ? client.delete(`/favorites/${id}`).then(() => favorited.value = false)
    : client.post(`/favorites/${id}`).then(() => favorited.value = true)
}

function doClose() {
  if (phase.value === 'start') return
  showBar.value = false
  phase.value = 'exit'
  setTimeout(() => store.close(), 350)
}


</script>

<template>
  <Teleport to="body">
    <div v-if="store.photoId" :class="['pv-bg', backdropOn ? 'pv-bg--on' : '']" @click="doClose" />
    <div v-if="store.photoId" class="pv-img-wrap">
      <div v-if="!src" class="pv-placeholder" />
      <img v-else :src="src" :class="imgClass" :style="imgStyle" @click.stop="showInfo = !showInfo" />
    </div>

    <!-- Top bar -->
    <div v-if="showBar" class="pv-topbar">
      <el-button circle :icon="'ArrowLeft'" @click="doClose" class="glass-btn" />
      <el-icon v-if="!previewReady" class="is-loading" :size="20" style="color:var(--el-text-color-secondary);margin-left:4px"><Loading /></el-icon>
      <div style="flex:1" />
      <el-button circle :icon="favorited ? 'StarFilled' : 'Star'" @click="toggleFav"
        :class="['glass-btn', favorited ? 'fav-active' : '']" />
      <a :href="originalUrl" download>
        <el-button circle :icon="'Download'" class="glass-btn" />
      </a>
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
  pointer-events: none;
}
.pv-placeholder {
  width: 100%; height: 100%;
  background: var(--el-bg-color);
}
.pv-img {
  object-fit: contain; border-radius: 0; will-change: transform;
}
.pv-img:not(.pv-img--anim) { transition: none; border-radius: 4px; }
.pv-img--anim {
  transition: transform .35s cubic-bezier(.25,.46,.45,.94), border-radius .35s ease, opacity .2s ease;
  border-radius: 0;
}
.pv-img--cover { object-fit: cover; }
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
