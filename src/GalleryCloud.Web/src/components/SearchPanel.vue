<script setup lang="ts">
import { ref } from 'vue'

const emit = defineEmits<{ search: [params: Record<string, string>] }>()

const q = ref('')
const from = ref('')
const to = ref('')
const format = ref('')
const device = ref('')

function doSearch() {
  const params: Record<string, string> = {}
  if (q.value) params.q = q.value
  if (from.value) params.from = from.value
  if (to.value) params.to = to.value
  if (format.value) params.format = format.value
  if (device.value) params.device = device.value
  emit('search', params)
}
</script>

<template>
  <div class="bg-white rounded-lg shadow p-4 space-y-3">
    <div class="flex gap-3">
      <input v-model="q" placeholder="搜索文件名..." class="flex-1 border rounded px-3 py-2"
        @keyup.enter="doSearch" />
      <button @click="doSearch" class="bg-blue-500 text-white px-4 py-2 rounded hover:bg-blue-600">搜索</button>
    </div>
    <div class="grid grid-cols-4 gap-3">
      <input v-model="from" type="date" placeholder="开始日期" class="border rounded px-3 py-1.5 text-sm" />
      <input v-model="to" type="date" placeholder="结束日期" class="border rounded px-3 py-1.5 text-sm" />
      <input v-model="format" placeholder="格式 (jpg,heic)" class="border rounded px-3 py-1.5 text-sm" />
      <input v-model="device" placeholder="设备型号" class="border rounded px-3 py-1.5 text-sm" />
    </div>
  </div>
</template>
