<script setup lang="ts">
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useSelectionStore } from '../stores/selectionStore'
import BatchToolbar from './BatchToolbar.vue'

const props = withDefaults(defineProps<{
  count?: number
  selectionSource?: string
  showHideInBatch?: boolean
}>(), { showHideInBatch: true })

const emit = defineEmits<{ 'batch-hide': [ids: string[]] }>()

const { columns: cols, zoomIn, zoomOut } = usePhotoGrid()
const store = useSelectionStore()
</script>

<template>
  <div class="pgt-row">
    <slot name="left">
      <template v-if="selectionSource">
        <template v-if="store.enabled">
          <BatchToolbar :show-hide="showHideInBatch" @batch-hide="(ids) => $emit('batch-hide', ids)" />
        </template>
        <template v-else>
          <el-button text size="small" @click="store.enable(selectionSource)">
            <el-icon><Select /></el-icon>选择
          </el-button>
        </template>
      </template>
    </slot>
    <template v-if="store.enabled">
      <el-button text size="small" @click="store.disable()">取消</el-button>
      <span class="pgt-count-selected">已选 {{ store.count }} 张</span>
    </template>
    <template v-else>
      <span v-if="count != null" class="pgt-count">{{ count }}张</span>
    </template>
    <div style="flex:1" />
    <slot name="right" />
    <el-button text :icon="'ZoomOut'" @click="zoomOut" :disabled="cols >= 12" class="pgt-btn" />
    <span class="pgt-cols">{{ cols }}</span>
    <el-button text :icon="'ZoomIn'" @click="zoomIn" :disabled="cols <= 3" class="pgt-btn" />
  </div>
</template>

<style>
.pgt-row { display: flex; align-items: center; gap: 8px; width: 100%; flex-wrap: wrap; }
.pgt-count { font-size: 13px; color: var(--el-text-color-secondary); white-space: nowrap; }
.pgt-count-selected { font-size: 13px; color: var(--el-color-primary); white-space: nowrap; font-weight: 600; }
.pgt-cols { font-size: 13px; color: var(--el-text-color-secondary); min-width: 24px; text-align: center; }
.pgt-btn { color: var(--el-text-color-secondary) !important; }
/* 带文本的图标按钮，图标与文字之间留间距（而非 icon prop 的纯图标按钮不受影响） */
.pgt-row .el-button:not(.pgt-btn) .el-icon { margin-right: 4px; }
/* 统一行内 flex 按钮的垂直居中 */
.pgt-row .el-button { display: inline-flex; align-items: center; }
</style>
