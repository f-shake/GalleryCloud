<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useInfiniteQuery } from '../composables/useInfiniteQuery'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore, toDateInt } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
const { items: photos, loading, hasMore, loadMore } = useInfiniteQuery('/favorites')

onMounted(() => loadMore())

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src,
    (photos.value as any[]).map(p => ({ id: p.id, takenAtDate: toDateInt(p.takenAt) })))
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
  <div style="padding:16px">
    <div style="display:flex;align-items:center;gap:8px;margin-bottom:12px">
      <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ columns }}列</span>
      <div style="flex:1" />
      <el-button-group size="small">
        <el-button icon="Minus" @click="zoomOut" :disabled="columns >= 12" />
        <el-button icon="Plus" @click="zoomIn" :disabled="columns <= 3" />
      </el-button-group>
    </div>

    <el-empty v-if="!loading && photos.length === 0 && !isScanning" description="暂无收藏" />

    <div v-else :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'0' }">
      <div v-for="p in photos" :key="p.id" class="thumb-cell" @click="onPhotoClick(p.id, $event)">
        <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
      </div>
    </div>

    <div v-if="loading" style="text-align:center;padding:24px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
  </div>
</template>
