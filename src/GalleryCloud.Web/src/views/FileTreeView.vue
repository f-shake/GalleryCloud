<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { toDateInt } from '../stores/photoViewStore'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'
import client from '../api/client'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'

interface FolderNode { name: string; path: string; rootId?: string; photoCount: number; subFolders: FolderNode[]; _key: string }
const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

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
const selRootId = ref('')
const photos = ref<any[]>([])
const loading = ref(false)
const treeLoading = ref(true)

// Group photos using YYYYMMDD integer arithmetic (same as timeline)
const photoGroups = computed(() => {
  void columns.value
  const level = groupLevel.value
  const groups: { label: string; photos: any[] }[] = []
  const items = photos.value
  if (level === 'none' || items.length === 0) {
    if (items.length > 0) groups.push({ label: '', photos: items })
    return groups
  }
  let lastKey = ''
  let currentLabel = ''
  let currentBatch: any[] = []
  function flush() {
    if (currentBatch.length > 0) {
      groups.push({ label: currentLabel, photos: currentBatch.splice(0) })
    }
  }
  for (const p of items) {
    const dateInt = toDateInt(p.takenAt)
    if (dateInt == null) { currentBatch.push(p); continue }
    const y = Math.floor(dateInt / 10000)
    const m = Math.floor((dateInt % 10000) / 100)
    const day = dateInt % 100
    const key = level === 'month'
      ? `${y}-${String(m).padStart(2, '0')}`
      : `${y}-${String(m).padStart(2, '0')}-${String(day).padStart(2, '0')}`
    if (key !== lastKey && lastKey !== '') flush()
    lastKey = key
    currentLabel = level === 'month'
      ? `${y}年${m}月`
      : `${y}年${m}月${day}日`
    currentBatch.push(p)
  }
  flush()
  return groups
})

onMounted(async () => {
  try {
    const r = await client.get('/folders')
    tree.value = addKeys(r.data)
  }
  catch { /* */ }
  finally { treeLoading.value = false }
})

function addKeys(nodes: FolderNode[]): FolderNode[] {
  return nodes.map(n => ({
    ...n,
    _key: n.rootId ? n.rootId + ':' + n.path : n.path,
    subFolders: addKeys(n.subFolders)
  }))
}

async function onNodeClick(node: FolderNode) {
  selPath.value = node.path
  selRootId.value = node.rootId || ''
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (node.rootId) params.set('rootId', node.rootId)
    const qs = params.toString() ? '?' + params.toString() : ''
    const r = await client.get(`/folders/${encodeURI(node.path)}${qs}`)
    photos.value = r.data
  }
  catch { /* */ }
  finally { loading.value = false }
}

const defaultExpanded = computed(() => tree.value.map(n => n._key))
</script>

<template>
  <div class="ft-layout">
    <div class="ft-tree-panel">
      <el-tree
        :data="tree"
        :props="{ children: 'subFolders', label: 'name' }"
        node-key="_key"
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
      <div style="margin-bottom:12px;flex-shrink:0">
        <PhotoGridToolbar :count="photos.length">
          <template #left>
            <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ selPath || (selRootId ? '' : '选择文件夹') }}</span>
          </template>
        </PhotoGridToolbar>
      </div>
      <el-empty v-if="!selPath && !selRootId && !isScanning" description="选择左侧文件夹" />
      <div v-else-if="loading" style="text-align:center;padding:32px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
      <PhotoGrid v-else :groups="photoGroups" :columns="columns" @photo-click="onPhotoClick" style="flex:1;min-height:0" />
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
.ft-photo-panel { flex: 1; overflow: hidden; padding: 8px; display: flex; flex-direction: column; }

/* Mobile: stacked layout — tree top 1/3, photos bottom */
@media (max-width: 767px) {
  .ft-layout { flex-direction: column; }
  .ft-tree-panel {
    width: 100%; flex: 0 0 33%; min-height: 120px;
    border-right: none;
    border-bottom: 1px solid var(--el-border-color-light);
    padding: 4px 0;
  }
  .ft-photo-panel { flex: 1; overflow: hidden; padding: 8px 12px; display: flex; flex-direction: column; }
}
</style>
