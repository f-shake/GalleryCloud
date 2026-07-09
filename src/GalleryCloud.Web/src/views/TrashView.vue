<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { useSelectionStore } from '../stores/selectionStore'
import client from '../api/client'
import { ElMessage, ElMessageBox } from 'element-plus'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import { useLongPressSelection } from '../composables/useLongPressSelection'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
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

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))
const { onTouchStart, onTouchMove, onTouchEnd } = useLongPressSelection()

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

function formatDeletedAt(dateStr: string): string {
  if (!dateStr) return ''
  const d = new Date(dateStr)
  if (isNaN(d.getTime())) return dateStr
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
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
            <BatchToolbar :show-hide="false" />
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

    <div v-else-if="photos.length" class="trash-body">
      <div
        v-for="p in photos"
        :key="p.id"
        class="trash-item"
        :class="{ 'trash-item--selected': selStore.enabled && selStore.selectedIds.has(p.id) }"
        @click="selStore.enabled ? selStore.toggle(p.id) : onPhotoClick(p.id, $event)"
      >
        <img :src="thumbUrl(p.id, 'grid', 400)" class="trash-thumb" loading="lazy" />
        <div v-if="selStore.enabled" class="trash-check" :class="{ 'trash-check--on': selStore.selectedIds.has(p.id) }">
          <svg v-if="selStore.selectedIds.has(p.id)" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="5,12 10,17 19,8" />
          </svg>
        </div>
        <div class="trash-overlay">
          <span class="trash-date">已隐藏 {{ formatDeletedAt(p.deletedAt) }}</span>
          <el-button v-if="!selStore.enabled" size="small" text style="color:#fff" @click.stop="restoreSingle(p.id)">恢复</el-button>
        </div>
      </div>
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
.trash-body {
  flex: 1; overflow-y: auto; padding: 8px;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 8px;
  align-content: start;
}
.trash-item {
  position: relative;
  border-radius: 6px;
  overflow: hidden;
  cursor: pointer;
  aspect-ratio: 1;
  background: var(--el-fill-color-light);
}
.trash-item--selected { outline: 3px solid var(--el-color-primary); outline-offset: -3px; }
.trash-thumb {
  width: 100%; height: 100%;
  object-fit: cover;
  opacity: 0.6;
}
.trash-check {
  position: absolute; top: 6px; left: 6px; z-index: 2;
  width: 22px; height: 22px; border-radius: 50%;
  border: 2px solid rgba(255,255,255,0.9);
  background: rgba(0,0,0,0.3);
  display: flex; align-items: center; justify-content: center;
  transition: background .15s;
  pointer-events: none;
}
.trash-check--on { background: var(--el-color-primary); border-color: var(--el-color-primary); }
.trash-overlay {
  position: absolute;
  bottom: 0; left: 0; right: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px;
  background: linear-gradient(transparent, rgba(0,0,0,0.7));
  font-size: 11px;
  color: #fff;
}
.trash-date { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
