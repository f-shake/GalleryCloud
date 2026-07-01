<script setup lang="ts">
import { ref, watch } from 'vue'
import client from '../api/client'

export interface FsEntry {
  name: string
  fullPath: string
  isDrive: boolean
}

export interface FsBrowseResult {
  currentPath: string
  entries: FsEntry[]
  parentPath: string | null
  isRoot: boolean
}

const props = defineProps<{
  modelValue: boolean
  initialPath?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  selected: [path: string]
}>()

const visible = ref(false)
const loading = ref(false)
const currentPath = ref('')
const entries = ref<FsEntry[]>([])
const parentPath = ref<string | null>(null)
const error = ref('')

watch(() => props.modelValue, (val) => {
  visible.value = val
  if (val) {
    loadPath(props.initialPath || '')
  }
})

async function loadPath(path: string) {
  loading.value = true
  error.value = ''
  try {
    if (!path) {
      // Load drives
      const r = await client.get('/admin/fs/drives')
      entries.value = r.data
      currentPath.value = ''
      parentPath.value = null
    } else {
      const r = await client.get('/admin/fs/browse', { params: { path } })
      const data: FsBrowseResult = r.data
      currentPath.value = data.currentPath
      entries.value = data.entries
      parentPath.value = data.parentPath
    }
  } catch (e: any) {
    error.value = e.response?.data?.error || e.message || '加载失败'
    entries.value = []
  } finally {
    loading.value = false
  }
}

function goUp() {
  if (parentPath.value != null) {
    loadPath(parentPath.value)
  }
}

function enterDir(entry: FsEntry) {
  loadPath(entry.fullPath)
}

function selectCurrent() {
  if (currentPath.value) {
    emit('selected', currentPath.value)
    visible.value = false
    emit('update:modelValue', false)
  }
}

function onClose() {
  visible.value = false
  emit('update:modelValue', false)
}
</script>

<template>
  <el-dialog
    :model-value="visible"
    title="选择目录"
    width="600px"
    :close-on-click-modal="false"
    @close="onClose"
    destroy-on-close
  >
    <!-- Current path -->
    <div style="margin-bottom:8px; display:flex; align-items:center; gap:4px">
      <el-button size="small" @click="loadPath('')" title="根目录">
        <el-icon><HomeFilled /></el-icon>
      </el-button>
      <el-button
        size="small"
        :disabled="!parentPath && !currentPath"
        @click="goUp"
        title="上级目录"
      >
        <el-icon><ArrowUpBold /></el-icon>
      </el-button>
      <el-tag type="info" style="flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-family:monospace">
        {{ currentPath || '（选择驱动器/根目录）' }}
      </el-tag>
    </div>

    <!-- Error -->
    <el-alert v-if="error" :title="error" type="error" show-icon style="margin-bottom:8px" closable />

    <!-- Loading -->
    <div v-if="loading" style="text-align:center; padding:32px">
      <el-icon class="is-loading" :size="24"><Loading /></el-icon>
      <div style="margin-top:8px; color:var(--el-text-color-secondary); font-size:13px">加载中...</div>
    </div>

    <!-- Directory list -->
    <div
      v-else-if="entries.length === 0"
      style="text-align:center; padding:32px; color:var(--el-text-color-placeholder)"
    >
      此目录下没有子目录
    </div>
    <div v-else style="border:1px solid var(--el-border-color-light); border-radius:4px; max-height:360px; overflow-y:auto">
      <div
        v-for="entry in entries"
        :key="entry.fullPath"
        class="folder-row"
        @click="enterDir(entry)"
      >
        <el-icon style="margin-right:6px; color:var(--el-color-warning)">
          <FolderOpened />
        </el-icon>
        <span style="flex:1">{{ entry.name }}</span>
        <el-button
          size="small"
          type="primary"
          text
          @click.stop="currentPath = entry.fullPath; selectCurrent()"
        >
          选择
        </el-button>
      </div>
    </div>

    <template #footer>
      <el-button @click="onClose">取消</el-button>
      <el-button
        type="primary"
        :disabled="!currentPath"
        @click="selectCurrent"
      >
        选择此目录
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.folder-row {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  cursor: pointer;
  border-bottom: 1px solid var(--el-border-color-light);
  transition: background 0.15s;
  user-select: none;
}
.folder-row:last-child {
  border-bottom: none;
}
.folder-row:hover {
  background: var(--el-color-primary-light-9);
}
</style>
