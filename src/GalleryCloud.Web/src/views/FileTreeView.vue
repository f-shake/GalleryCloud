<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

interface FolderNode { name: string; path: string; photoCount: number; subFolders: FolderNode[] }

const router = useRouter()
const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
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
  try { const r = await client.get(`/folders/${encodeURIComponent(node.path)}`); photos.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
}

const defaultExpanded = computed(() => tree.value.slice(0, 10).map(n => n.path))
</script>

<template>
  <div style="display:flex;height:100%">
    <div style="width:260px;border-right:1px solid var(--el-border-color-light);overflow-y:auto;padding:8px;flex-shrink:0">
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
          <span style="display:flex;align-items:center;justify-content:space-between;width:100%">
            <span style="display:flex;align-items:center;gap:4px">
              <el-icon><Folder /></el-icon>
              <span style="font-size:13px">{{ data.name }}</span>
            </span>
            <el-tag size="small" type="info" effect="plain">{{ data.photoCount }}</el-tag>
          </span>
        </template>
      </el-tree>
    </div>
    <div style="flex:1;overflow-y:auto;padding:16px">
      <div style="display:flex;align-items:center;gap:8px;margin-bottom:12px">
        <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ selPath || '选择文件夹' }}</span>
        <div style="flex:1" />
        <el-button-group size="small">
          <el-button icon="Minus" @click="zoomOut" :disabled="columns >= 12" />
          <el-button icon="Plus" @click="zoomIn" :disabled="columns <= 3" />
        </el-button-group>
      </div>
      <el-empty v-if="!selPath && !isScanning" description="选择左侧文件夹" />
      <div v-else-if="loading" style="text-align:center;padding:32px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
      <div v-else-if="photos.length" :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'0' }">
        <div v-for="p in photos" :key="p.id" class="thumb-cell" @click="onPhotoClick(p.id, $event)">
          <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
        </div>
      </div>
    </div>
  </div>
</template>
