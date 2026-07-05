<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'

interface SharePhoto {
  id: string; fileName: string; fileFormat: string
  width: number | null; height: number | null
}

const route = useRoute()
const token = route.params.token as string
const shareName = ref('')
const photos = ref<SharePhoto[]>([])
const loading = ref(true)
const error = ref('')

onMounted(async () => {
  try {
    const r = await fetch(`${import.meta.env.BASE_URL}api/public/shares/${token}`)
    if (!r.ok) { error.value = '分享不存在或已过期'; return }
    const data = await r.json()
    shareName.value = data.share?.name || ''
    photos.value = data.photos || []
  } catch { error.value = '加载失败' }
  finally { loading.value = false }
})

const base = import.meta.env.BASE_URL
function thumbUrl(id: string): string {
  return `${base}api/public/shares/${token}/photos/${id}/thumbnail?size=grid&w=400`
}
function fileUrl(id: string): string {
  return `${base}api/public/shares/${token}/photos/${id}/file`
}
</script>

<template>
  <div class="ps-wrap">
    <div v-if="loading" class="ps-loading">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
    </div>

    <div v-else-if="error" class="ps-error">{{ error }}</div>

    <template v-else>
      <div class="ps-header">
        <h2>{{ shareName }}</h2>
        <span class="ps-count">{{ photos.length }} 张照片</span>
      </div>

      <div class="ps-grid">
        <a v-for="p in photos" :key="p.id" :href="fileUrl(p.id)" target="_blank" class="ps-cell">
          <img :src="thumbUrl(p.id)" class="ps-img" loading="lazy" />
        </a>
      </div>
    </template>
  </div>
</template>

<style>
body { margin: 0; background: #f5f5f5; }
.ps-wrap { min-height: 100vh; padding: 16px; max-width: 1200px; margin: 0 auto; }
.ps-loading, .ps-error { display: flex; align-items: center; justify-content: center; min-height: 60vh; font-size: 16px; }
.ps-error { color: #e74c3c; }
.ps-header { margin-bottom: 16px; }
.ps-header h2 { margin: 0; font-size: 20px; }
.ps-count { font-size: 13px; color: #666; }
.ps-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 8px; }
.ps-cell { aspect-ratio: 1; overflow: hidden; border-radius: 6px; background: #eee; }
.ps-img { width: 100%; height: 100%; object-fit: cover; transition: transform .2s; }
.ps-img:hover { transform: scale(1.05); }
</style>
