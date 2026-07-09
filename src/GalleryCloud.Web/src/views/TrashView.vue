<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useSelectionStore } from '../stores/selectionStore'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import client from '../api/client'
import { ElMessage, ElMessageBox } from 'element-plus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import { useLongPressSelection } from '../composables/useLongPressSelection'
import { formatLocalDateTime } from '../utils/date'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'
import BatchToolbar from '../components/BatchToolbar.vue'

interface TrashPhoto {
  id: string; fileName: string; fileFormat: string
  width: number | null; height: number | null; orientation: number
  takenAt: string | null; deletedAt: string; fileSize: number
}

const selStore = useSelectionStore()
const photos = ref<TrashPhoto[]>([])
const total = ref(0)
const loading = ref(false)

const { columns } = usePhotoGrid()
const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))
const { onTouchStart, onTouchMove, onTouchEnd } = useLongPressSelection()

const gridEl = ref<HTMLElement | null>(null)
const gridWidth = ref(0)
let resizeObs: ResizeObserver | null = null

onMounted(() => {
  if (gridEl.value) {
    gridWidth.value = gridEl.value.clientWidth
    resizeObs = new ResizeObserver(entries => {
      gridWidth.value = entries[0]?.contentRect.width ?? 0
    })
    resizeObs.observe(gridEl.value)
  }
})
onUnmounted(() => resizeObs?.disconnect())

/** 每个缩略格的实际宽度 ≈ 容器宽度减去间隙再除以列数 */
const cellWidth = computed(() => {
  if (!gridWidth.value || !columns.value) return 0
  const gaps = (columns.value - 1) * 4
  return (gridWidth.value - gaps) / columns.value
})

watch(photos, (val) => {
  selStore.setViewPhotos(val.map(p => ({ id: p.id, takenAt: p.takenAt })))
}, { immediate: false })

async function loadTrash() {
  selStore.disable()
  loading.value = true
  try {
    const r = await client.get('/trash', { params: { page: 1, limit: 10000 } })
    photos.value = r.data.items
    total.value = r.data.total
  } catch { /* */ }
  finally { loading.value = false }
}

async function restoreSingle(id: string) {
  try {
    await client.patch(`/photos/${id}/restore`)
    ElMessage.success('已恢复')
    photos.value = photos.value.filter(p => p.id !== id)
    total.value--
  } catch { ElMessage.error('恢复失败，文件可能已不存在') }
}

async function batchRestore() {
  if (selStore.count === 0) {
    ElMessage.warning('请先选择照片')
    return
  }
  try {
    await ElMessageBox.confirm(`确定恢复选中的 ${selStore.count} 张照片？`, '恢复照片', {
      confirmButtonText: '确定', cancelButtonText: '取消', type: 'info',
    })
    await client.patch('/photos/batch/restore', { ids: Array.from(selStore.selectedIds) })
    ElMessage.success(`已恢复 ${selStore.count} 张照片`)
    selStore.disable()
    loadTrash()
  } catch { /* */ }
}

onMounted(loadTrash)
</script>

<template>
  <div class="trash-wrap" @touchstart="onTouchStart($event, '')" @touchmove="onTouchMove" @touchend="onTouchEnd">
    <div class="trash-toolbar">
      <PhotoGridToolbar :count="total">
        <template #left>
          <el-button v-if="!selStore.enabled" text size="small" @click="selStore.enable('trash')">
            <el-icon><Select /></el-icon>选择
          </el-button>
          <template v-else>
            <BatchToolbar :show-hide="false" :show-share="false" />
            <el-button size="small" type="primary" @click="batchRestore">
              <el-icon style="margin-right:2px"><Refresh /></el-icon>恢复选中
            </el-button>
          </template>
        </template>
      </PhotoGridToolbar>
    </div>

    <el-empty v-if="!loading && photos.length === 0" description="回收站为空" />

    <div v-if="loading" style="flex:1;display:flex;align-items:center;justify-content:center">
      <el-icon class="is-loading" :size="28"><Loading /></el-icon>
    </div>

    <div v-else-if="photos.length" ref="gridEl" class="trash-body">
      <PhotoGrid
        :photos="photos"
        :columns="columns"
        :selection-mode="selStore.enabled"
        :selected-ids="selStore.selectedIds"
        @photo-click="onPhotoClick"
        @selection-toggle="selStore.toggle"
      >
        <template #cell-footer="{ photo }">
          <div v-if="!selStore.enabled" class="trash-overlay" :class="{ 'trash-overlay--no-date': cellWidth < 120 }">
            <span v-if="cellWidth >= 120" class="trash-date">{{ formatLocalDateTime(photo.deletedAt) }}</span>
            <el-button size="small" text style="color:#fff" @click.stop="restoreSingle(photo.id)">恢复</el-button>
          </div>
        </template>
      </PhotoGrid>
    </div>
  </div>
</template>

<style scoped>
.trash-wrap { position: absolute; inset: 0; display: flex; flex-direction: column; }
.trash-toolbar {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 8px;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
}
.trash-body { flex: 1; min-height: 0; overflow: hidden; padding: 8px; }
/* dim thumbnails */
.trash-body :deep(.thumb-img) { opacity: 0.6; }
.trash-overlay {
  position: absolute; bottom: 0; left: 0; right: 0;
  display: flex; align-items: center; justify-content: space-between;
  padding: 4px 8px;
  background: linear-gradient(transparent, rgba(0,0,0,0.7));
  font-size: 11px; color: #fff;
  pointer-events: auto;
}
.trash-overlay--no-date { justify-content: flex-end; }
.trash-date { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
