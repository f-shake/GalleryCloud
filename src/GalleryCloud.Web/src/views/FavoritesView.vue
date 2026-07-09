<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useSelectionStore } from '../stores/selectionStore'
import { useInfiniteQuery } from '../composables/useInfiniteQuery'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import { useLongPressSelection } from '../composables/useLongPressSelection'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'
import BatchToolbar from '../components/BatchToolbar.vue'

const { columns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const selStore = useSelectionStore()
const { items: photos, loading, hasMore, loadMore } = useInfiniteQuery('/favorites')
const containerRef = ref<HTMLElement | null>(null)

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))
const { onTouchStart, onTouchMove, onTouchEnd } = useLongPressSelection()

watch(photos, (val) => {
  selStore.setViewPhotos(val.map((p: any) => ({ id: p.id, takenAt: p.takenAt })))
}, { immediate: true })

onMounted(() => loadMore())

/** 批量隐藏后从列表中移除 */
function onBatchHide(hiddenIds: string[]) {
  const ids = new Set(hiddenIds)
  photos.value = photos.value.filter((p: any) => !ids.has(p.id))
}

/** 单张隐藏后从列表中移除 */
function onPhotoHidden(e: Event) {
  const id = (e as CustomEvent).detail.id
  const idx = photos.value.findIndex((p: any) => p.id === id)
  if (idx >= 0) photos.value.splice(idx, 1)
}

function onScroll() {
  if (!containerRef.value) return
  const el = containerRef.value
  if (el.scrollHeight - el.scrollTop - el.clientHeight < 500 && hasMore.value) loadMore()
}

onMounted(() => {
  loadMore()
  containerRef.value?.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('photo-hidden', onPhotoHidden)
})
onUnmounted(() => {
  containerRef.value?.removeEventListener('scroll', onScroll)
  window.removeEventListener('photo-hidden', onPhotoHidden)
})
</script>

<template>
  <div class="fv-wrap" @touchstart="onTouchStart($event, '')" @touchmove="onTouchMove" @touchend="onTouchEnd">
    <div class="fv-toolbar">
      <PhotoGridToolbar :count="photos.length" selectionSource="favorites" @batch-hide="onBatchHide" />
    </div>

    <el-empty v-if="!loading && photos.length === 0 && !isScanning" description="暂无收藏" />

    <PhotoGrid
      v-else
      :photos="photos as any[]"
      :columns="columns"
      :selection-mode="selStore.enabled"
      :selected-ids="selStore.selectedIds"
      @photo-click="onPhotoClick"
      @selection-toggle="selStore.toggle"
      style="flex:1;min-height:0"
    />

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
