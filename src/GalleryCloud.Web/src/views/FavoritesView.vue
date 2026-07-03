<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useInfiniteQuery } from '../composables/useInfiniteQuery'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'

const { columns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const { items: photos, loading, hasMore, loadMore } = useInfiniteQuery('/favorites')
const containerRef = ref<HTMLElement | null>(null)

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

onMounted(() => loadMore())

function onScroll() {
  if (!containerRef.value) return
  const el = containerRef.value
  if (el.scrollHeight - el.scrollTop - el.clientHeight < 500 && hasMore.value) loadMore()
}

onMounted(() => {
  containerRef.value?.addEventListener('scroll', onScroll, { passive: true })
})
onUnmounted(() => {
  containerRef.value?.removeEventListener('scroll', onScroll)
})
</script>

<template>
  <div class="fv-wrap">
    <div class="fv-toolbar">
      <PhotoGridToolbar :count="photos.length" />
    </div>

    <el-empty v-if="!loading && photos.length === 0 && !isScanning" description="暂无收藏" />

    <PhotoGrid v-else :photos="photos as any[]" :columns="columns" @photo-click="onPhotoClick" style="flex:1;min-height:0" />

    <div v-if="loading && photos.length === 0" class="fv-state-overlay"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
    <div v-else-if="isScanning && photos.length === 0" class="fv-state-overlay" style="color:var(--el-text-color-secondary)">扫描进行中...</div>

    <div v-if="loading && photos.length > 0" style="text-align:center;padding:24px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
  </div>
</template>

<style>
.fv-wrap { position: absolute; inset: 0; display: flex; flex-direction: column; }

.fv-toolbar {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 8px;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
}

.fv-state-overlay {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  z-index: 5; pointer-events: none;
}
</style>
