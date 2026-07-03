<script setup lang="ts">
import { usePhotoGrid } from '../composables/usePhotoGrid'

defineProps<{ count?: number }>()

const { columns: cols, zoomIn, zoomOut } = usePhotoGrid()
</script>

<template>
  <div class="pgt-row">
    <slot name="left" />
    <span v-if="count != null" class="pgt-count">{{ count }}张</span>
    <div style="flex:1" />
    <slot name="right" />
    <el-button text :icon="'ZoomOut'" @click="zoomOut" :disabled="cols >= 12" class="pgt-btn" />
    <span class="pgt-cols">{{ cols }}</span>
    <el-button text :icon="'ZoomIn'" @click="zoomIn" :disabled="cols <= 3" class="pgt-btn" />
  </div>
</template>

<style>
.pgt-row { display: flex; align-items: center; gap: 8px; width: 100%; }
.pgt-count { font-size: 13px; color: var(--el-text-color-secondary); white-space: nowrap; }
.pgt-cols { font-size: 13px; color: var(--el-text-color-secondary); min-width: 24px; text-align: center; }
.pgt-btn { color: var(--el-text-color-secondary) !important; }
</style>
