<script setup lang="ts">
import { computed } from 'vue'
import { thumbUrl } from '../composables/useThumbnailUrl'

interface GridPhoto { id: string }
interface Group { label?: string; photos: GridPhoto[] }

const props = withDefaults(defineProps<{
  /** Flat photo list (alternative to `groups`) */
  photos?: GridPhoto[]
  /** Grouped photos with optional labels (overrides `photos`) */
  groups?: Group[]
  /** Number of grid columns */
  columns: number
}>(), {
  photos: () => [],
  groups: undefined,
})

defineEmits<{
  'photo-click': [id: string, event: MouseEvent]
}>()

const displayGroups = computed<Group[]>(() => {
  if (props.groups) return props.groups
  return [{ photos: props.photos }]
})
</script>

<template>
  <div>
    <template v-for="(g, gi) in displayGroups" :key="gi">
      <div v-if="g.label" class="pg-header">
        <el-tag type="info" size="large">{{ g.label }}</el-tag>
      </div>
      <div
        class="photo-grid"
        :style="{
          display: 'grid',
          gridTemplateColumns: `repeat(${columns}, 1fr)`,
          gap: '4px',
          paddingBottom: '4px',
        }"
      >
        <div v-for="p in g.photos" :key="p.id" class="thumb-cell" @click="(e: MouseEvent) => $emit('photo-click', p.id, e)">
          <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
        </div>
      </div>
    </template>
  </div>
</template>

<style>
.pg-header { padding: 6px 0 4px 0; }
</style>
