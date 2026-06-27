<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import client from '../../api/client'

interface YearEntry { year: number; count: number }

const years = ref<YearEntry[]>([])
defineEmits<{ jumpTo: [year: number] }>()

onMounted(async () => {
  try {
    const res = await client.get('/timeline/years')
    years.value = res.data
  } catch { /* ignore */ }
})

const maxCount = computed(() => Math.max(...years.value.map(y => y.count), 1))

function handleClick(e: MouseEvent) {
  const el = e.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  const ratio = (e.clientY - rect.top) / rect.height
  const idx = Math.floor(ratio * years.value.length)
  if (years.value[idx]) {
    // emit jump
  }
}
</script>

<template>
  <div v-if="years.length > 0"
    class="fixed right-2 top-1/2 -translate-y-1/2 w-10 h-72 flex flex-col items-center gap-0.5 cursor-pointer z-20"
    @click="handleClick"
  >
    <div
      v-for="y in years"
      :key="y.year"
      class="w-full flex items-center gap-1"
    >
      <span class="text-[9px] text-gray-400 w-8 text-right leading-none">{{ y.year }}</span>
      <div
        class="flex-1 rounded-r"
        :style="{
          height: Math.max(2, (y.count / maxCount) * 12) + 'px',
          backgroundColor: `rgba(59,130,246,${0.3 + (y.count / maxCount) * 0.7})`
        }"
      />
    </div>
  </div>
</template>
