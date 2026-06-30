<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

interface FolderNode { name: string; path: string; photoCount: number; subFolders: FolderNode[] }

const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()

// Pinch zoom for photo grid
let pinchStart = 0, pinchEnd = 0
function onTouchStart(e: TouchEvent) {
  if (e.touches.length === 2) {
    pinchStart = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY)
    pinchEnd = pinchStart
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2 && pinchStart > 0) { e.preventDefault(); pinchEnd = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY) }
}
function onTouchEnd() {
  if (pinchStart > 0 && Math.abs(pinchEnd - pinchStart) > 20) { if (pinchEnd > pinchStart) zoomIn(); else zoomOut() }
  pinchStart = 0; pinchEnd = 0
}
const tree = ref<FolderNode[]>([])
const selPath = ref('')
const photos = ref<any[]>([])
const loading = ref(false)
const treeLoading = ref(true)

onMounted(async () => {
  try { const r = await client.get('/folders'); tree.value = r.data }
  catch { /* */ }
  finally { treeLoading.value = false }
})

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src)
}

async function onNodeClick(node: FolderNode) {
  selPath.value = node.path
  loading.value = true
  try { const r = await client.get(`/folders/${encodeURI(node.path)}`); photos.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
}

const defaultExpanded = computed(() => tree.value.slice(0, 10).map(n => n.path))
</script>

<template>
  <div class="ft-layout">
    <div class="ft-tree-panel">
      <el-tree
        :data="tree"
        :props="{ children: 'subFolders', label: 'name' }"
        node-key="path"
        :default-expanded-keys="defaultExpanded"
        @node-click="onNodeClick"
        :loading="treeLoading"
        style="background:transparent"
      >
        <template #default="{ data }">
          <span style="display:flex;align-items:center;gap:4px;overflow:hidden;width:100%">
            <el-icon style="flex-shrink:0"><Folder /></el-icon>
            <span style="font-size:13px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1;min-width:0">{{ data.name }}</span>
            <el-tag size="small" type="info" effect="plain" style="flex-shrink:0">{{ data.photoCount }}</el-tag>
          </span>
        </template>
      </el-tree>
    </div>
    <div class="ft-photo-panel" @touchstart="onTouchStart" @touchmove="onTouchMove" @touchend="onTouchEnd">
      <div style="margin-bottom:12px">
        <PhotoGridToolbar :count="photos.length">
          <template #left>
            <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ selPath || '选择文件夹' }}</span>
          </template>
        </PhotoGridToolbar>
      </div>
      <el-empty v-if="!selPath && !isScanning" description="选择左侧文件夹" />
      <div v-else-if="loading" style="text-align:center;padding:32px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
      <div v-else-if="photos.length" :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px' }">
        <div v-for="p in photos" :key="p.id" class="thumb-cell" @click="onPhotoClick(p.id, $event)">
          <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
        </div>
      </div>
    </div>
  </div>
</template>

<style>
.el-tree-node__content { overflow: hidden; }

/* Desktop: side-by-side layout */
.ft-layout { display: flex; height: 100%; }
.ft-tree-panel {
  width: 260px; flex-shrink: 0; overflow-y: auto;
  border-right: 1px solid var(--el-border-color-light);
  padding: 4px 8px;
}
.ft-photo-panel { flex: 1; overflow-y: auto; padding: 8px; }

/* Mobile: stacked layout — tree top 1/3, photos bottom */
@media (max-width: 767px) {
  .ft-layout { flex-direction: column; }
  .ft-tree-panel {
    width: 100%; flex: 0 0 33%; min-height: 120px;
    border-right: none;
    border-bottom: 1px solid var(--el-border-color-light);
  }
  .ft-photo-panel { flex: 1; overflow-y: auto; }
}
</style>
