<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useTimeline } from '../composables/useTimeline'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

const router = useRouter()
const viewStore = usePhotoViewStore()
const { columns, groupLevel, zoomIn, zoomOut, setColumns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const { groups, loading, hasMore, loadMore } = useTimeline(groupLevel, columns)
const gridAnimating = ref(false)

watch(columns, () => {
  gridAnimating.value = true
  setTimeout(() => { gridAnimating.value = false }, 300)
})

onMounted(() => loadMore())

let pinchStart = 0
let pinchEnd = 0

function onTouchStart(e: TouchEvent) {
  if (e.touches.length === 2) {
    pinchStart = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
    pinchEnd = pinchStart
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2 && pinchStart > 0) {
    e.preventDefault()
    pinchEnd = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    )
  }
}
function onTouchEnd() {
  if (pinchStart > 0 && Math.abs(pinchEnd - pinchStart) > 20) {
    if (pinchEnd > pinchStart) zoomIn()
    else zoomOut()
  }
  pinchStart = 0
  pinchEnd = 0
}

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src)
}

let scrollEl: HTMLElement | null = null
onMounted(() => {
  scrollEl = document.querySelector('.app-main')
  scrollEl?.addEventListener('scroll', onScroll, { passive: true })
})
onUnmounted(() => {
  scrollEl?.removeEventListener('scroll', onScroll)
})

function onScroll() {
  if (!scrollEl) return
  if (scrollEl.scrollHeight - scrollEl.scrollTop - scrollEl.clientHeight < 500 && hasMore.value) loadMore()
}
</script>

<template>
  <div style="touch-action:pan-y;padding:16px" @touchstart="onTouchStart" @touchmove="onTouchMove" @touchend="onTouchEnd">
    <div style="display:flex;align-items:center;gap:8px;margin-bottom:16px">
      <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ columns }}列 · {{ groupLevel === 'day' ? '按天' : groupLevel === 'month' ? '按月' : '平铺' }}</span>
      <div style="flex:1" />
      <el-button-group size="small">
        <el-button :icon="'Minus'" @click="zoomOut" :disabled="columns >= 12" />
        <el-button :icon="'Plus'" @click="zoomIn" :disabled="columns <= 3" />
      </el-button-group>
    </div>

    <el-empty v-if="groups.length === 0 && !loading && !isScanning" description="暂无照片" />

    <div v-for="(group, i) in groups" :key="i" :style="{ marginBottom: group.label ? '24px' : '0' }">
      <div v-if="group.label" style="position:sticky;top:0;z-index:10;background:var(--el-bg-color-page);padding:8px 0;margin-bottom:8px">
        <el-tag type="info" size="large">{{ group.label }}</el-tag>
      </div>
      <div :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'0' }"
        :class="{ 'grid-fade': gridAnimating }">
        <div v-for="p in group.photos" :key="p.id"
          class="thumb-cell"
          @click="onPhotoClick(p.id, $event)">
          <img
            v-lazy-img="thumbUrl(p.id, 'grid', 400)"
            class="thumb-img"
          />
        </div>
      </div>
    </div>

    <div v-if="loading" style="text-align:center;padding:24px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
  </div>
</template>

<style>
.grid-fade { animation: gridFade .25s ease; }
@keyframes gridFade { from { opacity: .4; } to { opacity: 1; } }
</style>
