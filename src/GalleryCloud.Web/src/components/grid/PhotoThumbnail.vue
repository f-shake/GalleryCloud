<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  photo: { id: string; fileName: string; width?: number | null; height?: number | null }
  thumbWidth: number
}>()

const emit = defineEmits<{ click: [id: string] }>()

const thumbUrl = computed(() =>
  `/api/photos/${props.photo.id}/thumbnail?size=grid&w=${props.thumbWidth}`
)

const aspectRatio = computed(() => {
  if (props.photo.width && props.photo.height)
    return props.photo.width / props.photo.height
  return 1
})
</script>

<template>
  <div
    class="cursor-pointer overflow-hidden rounded bg-gray-100 hover:ring-2 hover:ring-blue-400 transition"
    :style="{ aspectRatio: aspectRatio }"
    @click="emit('click', photo.id)"
  >
    <img
      :src="thumbUrl"
      :alt="photo.fileName"
      loading="lazy"
      class="w-full h-full object-cover"
    />
  </div>
</template>
