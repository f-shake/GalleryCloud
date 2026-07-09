<script setup lang="ts">
import { ref } from 'vue'
import { useSelectionStore, type DatePreset } from '../stores/selectionStore'
import { ElMessageBox, ElMessage } from 'element-plus'
import client from '../api/client'
import ShareDialog from './ShareDialog.vue'

const props = withDefaults(defineProps<{
  showHide?: boolean
  onPreset?: (preset: DatePreset) => void | Promise<void>
}>(), {
  showHide: true,
})

const store = useSelectionStore()

const emit = defineEmits<{
  'batch-hide': [ids: string[]]
}>()

const showShareDialog = ref(false)
const loadingPreset = ref<DatePreset | null>(null)

async function selectPreset(preset: DatePreset) {
  loadingPreset.value = preset
  try {
    if (props.onPreset) {
      await props.onPreset(preset)
    } else {
      await store.selectByDatePreset(preset)
    }
  } finally {
    loadingPreset.value = null
  }
}

async function batchHide() {
  if (store.count === 0) {
    ElMessage.warning('请先选择照片')
    return
  }
  try {
    await ElMessageBox.confirm(`确定隐藏选中的 ${store.count} 张照片？`, '隐藏照片', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      type: 'warning',
    })
    const hiddenIds = Array.from(store.selectedIds)
    await client.patch('/photos/batch/delete', { ids: hiddenIds })
    ElMessage.success(`已隐藏 ${store.count} 张照片`)
    store.disable()
    emit('batch-hide', hiddenIds)
  } catch { /* cancelled or error */ }
}

function openShare() {
  if (store.count === 0) {
    ElMessage.warning('请先选择照片')
    return
  }
  showShareDialog.value = true
}
</script>

<template>
  <div class="batch-toolbar">
    <span class="batch-label">全选范围：</span>
    <el-button size="small" :loading="loadingPreset === 'today'" :disabled="loadingPreset !== null" @click="selectPreset('today')">今天</el-button>
    <el-button size="small" :loading="loadingPreset === 'month'" :disabled="loadingPreset !== null" @click="selectPreset('month')">本月</el-button>
    <el-button size="small" :loading="loadingPreset === 'year'" :disabled="loadingPreset !== null" @click="selectPreset('year')">今年</el-button>
    <el-button size="small" :loading="loadingPreset === 'all'" :disabled="loadingPreset !== null" @click="selectPreset('all')">全部</el-button>
    <template v-if="store.count > 0">
      <div class="batch-sep" />
      <el-button v-if="showHide" size="small" type="danger" plain @click="batchHide">
        <el-icon style="margin-right:2px"><Delete /></el-icon>隐藏
      </el-button>
      <el-button size="small" type="primary" plain @click="openShare">
        <el-icon style="margin-right:2px"><Share /></el-icon>分享
      </el-button>
    </template>

    <ShareDialog
      v-model="showShareDialog"
      :photo-ids="Array.from(store.selectedIds)"
      @done="store.disable()"
    />
  </div>
</template>

<style scoped>
.batch-toolbar {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}
.batch-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}
.batch-sep {
  width: 1px;
  height: 20px;
  background: var(--el-border-color-light);
  margin: 0 4px;
}
</style>
