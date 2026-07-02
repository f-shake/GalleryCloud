<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore, toDateInt } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

interface FolderNode { name: string; path: string; rootId?: string; photoCount: number; subFolders: FolderNode[]; _key: string }

const viewStore = usePhotoViewStore()
const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
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
const selRootId = ref('')
const photos = ref<any[]>([])
const loading = ref(false)
const treeLoading = ref(true)

// Group photos using same logic as timeline
const photoGroups = computed(() => {
  void columns.value // force re-evaluate when columns change
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
    if (!p.takenAt) { currentBatch.push(p); continue }
    const d = new Date(p.takenAt)
    const key = level === 'month'
      ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
      : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    if (key !== lastKey && lastKey !== '') flush()
    lastKey = key
    currentLabel = level === 'month'
      ? `${d.getFullYear()}年${d.getMonth() + 1}月`
      : `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
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

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src,
    photos.value.map((p: any) => ({ id: p.id, takenAtDate: toDateInt(p.takenAt) })))
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
      <div style="margin-bottom:12px">
        <PhotoGridToolbar :count="photos.length">
          <template #left>
            <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ selPath || (selRootId ? '' : '选择文件夹') }}</span>
          </template>
        </PhotoGridToolbar>
      </div>
      <el-empty v-if="!selPath && !selRootId && !isScanning" description="选择左侧文件夹" />
      <div v-else-if="loading" style="text-align:center;padding:32px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
      <template v-for="(g, gi) in photoGroups" :key="gi">
        <div v-if="g.label" class="ft-group-header">
          <el-tag type="info" size="large">{{ g.label }}</el-tag>
        </div>
        <div class="photo-grid" :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px' }">
          <div v-for="p in g.photos" :key="p.id" class="thumb-cell" @click="onPhotoClick(p.id, $event)">
            <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style>
.el-tree-node__content { overflow: hidden; }
.ft-group-header { padding: 6px 0 4px 0; }

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
    padding: 4px 0;
  }
  .ft-photo-panel { flex: 1; overflow-y: auto; padding: 8px 12px; }
  .ft-photo-panel .photo-grid { margin: 0 -12px; width: calc(100% + 24px); }
}
</style>
