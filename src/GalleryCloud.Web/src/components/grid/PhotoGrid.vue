<script setup lang="ts">
import PhotoThumbnail from './PhotoThumbnail.vue'
import type { PhotoSummary } from '../../types'

defineProps<{
  photos: PhotoSummary[]
  columns: number
}>()

const emit = defineEmits<{ photoClick: [id: string] }>()
</script>

<template>
  <div
    class="grid gap-1"
    :style="{ gridTemplateColumns: `repeat(${columns}, 1fr)` }"
  >
    <PhotoThumbnail
      v-for="photo in photos"
      :key="photo.id"
      :photo="photo"
      :thumb-width="Math.ceil(400 / columns * 3)"
      @click="emit('photoClick', photo.id)"
    />
  </div>
</template>
