<script setup lang="ts">
import { ref, onMounted } from 'vue'
import client from '../api/client'

interface FolderNode {
  name: string
  path: string
  photoCount: number
  subFolders: FolderNode[]
}

const tree = ref<FolderNode[]>([])
const expanded = ref<Set<string>>(new Set())
const loading = ref(true)
const emit = defineEmits<{ select: [path: string] }>()

onMounted(async () => {
  try {
    const res = await client.get('/folders')
    tree.value = res.data
  } catch { /* ignore */ }
  finally { loading.value = false }
})

function toggle(path: string) {
  if (expanded.value.has(path)) expanded.value.delete(path)
  else expanded.value.add(path)
  expanded.value = new Set(expanded.value)
}
</script>

<template>
  <div v-if="loading" class="text-gray-500 p-4">加载中...</div>
  <div v-else class="text-sm">
    <template v-for="node in tree" :key="node.path">
      <div class="flex items-center gap-1 py-1 px-2 hover:bg-gray-100 cursor-pointer"
        @click="node.subFolders.length ? toggle(node.path) : emit('select', node.path)">
        <span class="text-xs w-4">{{ node.subFolders.length ? (expanded.has(node.path) ? '▾' : '▸') : '📁' }}</span>
        <span>{{ node.name }}</span>
        <span class="text-gray-400 text-xs ml-auto">{{ node.photoCount }}</span>
      </div>
      <template v-if="expanded.has(node.path)">
        <div v-for="sub in node.subFolders" :key="sub.path" class="ml-4 flex items-center gap-1 py-1 px-2 hover:bg-gray-100 cursor-pointer"
          @click="sub.subFolders.length ? toggle(sub.path) : emit('select', sub.path)">
          <span class="text-xs w-4">{{ sub.subFolders.length ? (expanded.has(sub.path) ? '▾' : '▸') : '📁' }}</span>
          <span>{{ sub.name }}</span>
          <span class="text-gray-400 text-xs ml-auto">{{ sub.photoCount }}</span>
        </div>
      </template>
    </template>
  </div>
</template>
