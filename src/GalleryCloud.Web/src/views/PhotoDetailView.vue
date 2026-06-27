<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'

const store = usePhotoViewStore()

const photo = ref<any>(null)
const favorited = ref(false)
const showInfo = ref(false)
const phase = ref<'start' | 'expand' | 'done' | 'exit'>('start')
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
  if (phase.value === 'start' || phase.value === 'exit') return { ...base, transform: startTransform.value }
  return base
})

const imgClass = computed(() => phase.value === 'start' ? 'pv-img' : 'pv-img pv-img--anim')
const backdropOn = computed(() => phase.value === 'done')

const src = computed(() => phase.value === 'done' && previewReady.value ? previewSrc.value : gridSrc.value)
const originalUrl = computed(() => `/api/photos/${store.photoId}/file?token=${localStorage.getItem('token') || ''}`)

watch(() => store.open, async (val) => {
  if (!val || !store.photoId) return
  const id = store.photoId
  gridSrc.value = thumbUrl(id, 'grid', 800)
  previewReady.value = false
  photo.value = null
  favorited.value = false
  showInfo.value = false
  phase.value = 'start'

  // Wait for DOM, then expand
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))
  phase.value = 'expand'

  // Load metadata
  try { const res = await client.get(`/photos/${id}`); photo.value = res.data } catch { /* */ }

  // Preload preview
  previewSrc.value = thumbUrl(id, 'preview', 2560)
  const img = new Image()
  img.onload = () => { previewReady.value = true; phase.value = 'done' }
  img.src = previewSrc.value
})

function toggleFav() {
  const id = store.photoId!
  favorited.value ? client.delete(`/favorites/${id}`).then(() => favorited.value = false)
    : client.post(`/favorites/${id}`).then(() => favorited.value = true)
}

function doClose() {
  if (phase.value === 'start') return
  phase.value = 'exit'
  setTimeout(() => store.close(), 350)
}

onMounted(() => { window.addEventListener('popstate', store.onPopState) })
onUnmounted(() => { window.removeEventListener('popstate', store.onPopState) })
</script>

<template>
  <Teleport to="body">
    <div v-if="store.photoId" :class="['pv-bg', backdropOn ? 'pv-bg--on' : '']" @click="doClose" />
    <div v-if="store.photoId" class="pv-img-wrap">
      <img :src="src" :class="imgClass" :style="imgStyle" @click.stop="showInfo = !showInfo" />
    </div>

    <!-- Top bar -->
    <div v-if="phase === 'done'" class="pv-topbar">
      <el-button circle :icon="'ArrowLeft'" @click="doClose" class="glass-btn" />
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
.pv-img {
  object-fit: contain; border-radius: 0; will-change: transform;
}
.pv-img:not(.pv-img--anim) { transition: none; border-radius: 4px; }
.pv-img--anim {
  transition: transform .35s cubic-bezier(.25,.46,.45,.94), border-radius .35s ease;
  border-radius: 0;
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
