<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import { usePhotoViewStore } from '../stores/photoViewStore'
import PhotoDetailView from './PhotoDetailView.vue'

interface SharePhoto {
  id: string; fileName: string; fileFormat: string
  width: number | null; height: number | null
}

interface ShareInfo {
  id: string; name: string; token: string
  expiresAt: string | null; photoCount: number
  allowDownload: boolean; allowMetadata: boolean
}

const route = useRoute()
const token = route.params.token as string

const shareName = ref('')
const photos = ref<SharePhoto[]>([])
const loading = ref(true)
const error = ref('')
const shareInfo = ref<ShareInfo | null>(null)

const store = usePhotoViewStore()

/** 分享页的点击处理：与 usePhotoClick 功能相同，但传递丰富的 item 数据给 store */
function onPhotoClick(id: string, e: MouseEvent) {
  const el = e.currentTarget as HTMLElement
  const img = el.querySelector('img')
  const rect = img
    ? { x: img.getBoundingClientRect().left, y: img.getBoundingClientRect().top, width: img.width, height: img.height }
    : { x: el.offsetLeft, y: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight }
  // 传递完整 photo 数据（fileName 等），供 PhotoDetailView 分享模式使用
  const items = photos.value.map(p => ({ ...p, takenAtDate: null })) as any
  store.show(id, rect, img?.src, items)
}

onMounted(async () => {
  try {
    const r = await fetch(`${import.meta.env.BASE_URL}api/public/shares/${token}`)
    if (!r.ok) { error.value = '分享不存在或已过期'; return }
    const data = await r.json()
    shareInfo.value = data.share || null
    shareName.value = data.share?.name || ''
    photos.value = data.photos || []
  } catch { error.value = '加载失败' }
  finally { loading.value = false }
})

function fileSizeLabel(w: number | null, h: number | null): string {
  if (w && h) return `${w} × ${h}`
  return ''
}

function thumbUrl(id: string): string {
  return `${import.meta.env.BASE_URL}api/public/shares/${token}/photos/${id}/thumbnail?size=grid&w=400`
}

const shareToken = computed(() => token)
const allowDownload = computed(() => shareInfo.value?.allowDownload ?? true)
const allowMetadata = computed(() => shareInfo.value?.allowMetadata ?? true)
</script>

<template>
  <div class="ps-wrap">
    <!-- 加载中 -->
    <div v-if="loading" class="ps-loading">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
    </div>

    <!-- 错误 -->
    <div v-else-if="error" class="ps-error">{{ error }}</div>

    <!-- 内容 -->
    <template v-else>
      <div class="ps-header">
        <h2>{{ shareName }}</h2>
        <span class="ps-count">{{ photos.length }} 张照片</span>
      </div>

      <div class="ps-grid">
        <div
          v-for="p in photos"
          :key="p.id"
          class="ps-cell"
          @click="onPhotoClick(p.id, $event)"
        >
          <img :src="thumbUrl(p.id)" class="ps-img" loading="lazy" />
          <div class="ps-cell-info">{{ fileSizeLabel(p.width, p.height) }}</div>
        </div>
      </div>
    </template>

    <!-- 复用 PhotoDetailView 预览 -->
    <PhotoDetailView
      :share-token="shareToken"
      :allow-download="allowDownload"
      :allow-metadata="allowMetadata"
    />
  </div>
</template>

<style>
body {
  margin: 0;
  background: var(--el-bg-color-page);
  color: var(--el-text-color-primary);
  color-scheme: light dark;
}
.ps-wrap { min-height: 100vh; padding: 16px; max-width: 1200px; margin: 0 auto; }
.ps-loading, .ps-error { display: flex; align-items: center; justify-content: center; min-height: 60vh; font-size: 16px; }
.ps-error { color: var(--el-color-danger); }
.ps-header { margin-bottom: 16px; }
.ps-header h2 { margin: 0; font-size: 20px; }
.ps-count { font-size: 13px; color: var(--el-text-color-secondary); }
.ps-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 8px; }
.ps-cell {
  position: relative;
  aspect-ratio: 1; overflow: hidden; border-radius: 6px;
  background: var(--el-fill-color-light);
  cursor: pointer;
}
.ps-cell:hover .ps-img { transform: scale(1.05); }
.ps-img { width: 100%; height: 100%; object-fit: cover; transition: transform .2s; display: block; }
.ps-cell-info {
  position: absolute; bottom: 0; left: 0; right: 0;
  padding: 4px 8px;
  background: linear-gradient(transparent, rgba(0,0,0,0.6));
  color: #fff;
  font-size: 11px;
  text-align: right;
  opacity: 0;
  transition: opacity .2s;
}
.ps-cell:hover .ps-cell-info { opacity: 1; }
</style>
