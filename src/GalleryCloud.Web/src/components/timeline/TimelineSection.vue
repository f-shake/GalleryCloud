<script setup lang="ts">
import PhotoGrid from '../grid/PhotoGrid.vue'
import type { PhotoSummary } from '../../types'

defineProps<{
  label?: string
  photos: PhotoSummary[]
  columns: number
}>()

const emit = defineEmits<{ photoClick: [id: string] }>()
</script>

<template>
  <div class="mb-6">
    <div v-if="label" class="sticky top-0 bg-gray-50 z-10 py-2 px-1 mb-2">
      <span class="text-sm font-semibold text-gray-600">{{ label }}</span>
    </div>
    <PhotoGrid :photos="photos" :columns="columns" @photo-click="emit('photoClick', $event)" />
  </div>
</template>
