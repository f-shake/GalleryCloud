<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import PhotoGrid from '../components/PhotoGrid.vue'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoDetailView from './PhotoDetailView.vue'

interface SharePhoto {
  id: string; fileName: string; fileFormat: string
  width: number | null; height: number | null
  thumbUrl?: string
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

const { columns } = usePhotoGrid()
const store = usePhotoViewStore()

function onPhotoClick(id: string, e: MouseEvent) {
  const el = e.currentTarget as HTMLElement
  const img = el.querySelector('img')
  const rect = img
    ? { x: img.getBoundingClientRect().left, y: img.getBoundingClientRect().top, width: img.width, height: img.height }
    : { x: el.offsetLeft, y: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight }
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
    photos.value = (data.photos || []).map((p: any) => ({
      ...p,
      thumbUrl: `${import.meta.env.BASE_URL}api/public/shares/${token}/photos/${p.id}/thumbnail?size=grid&w=400`,
    }))
  } catch { error.value = '加载失败' }
  finally { loading.value = false }
})

const shareToken = computed(() => token)
const allowDownload = computed(() => shareInfo.value?.allowDownload ?? true)
const allowMetadata = computed(() => shareInfo.value?.allowMetadata ?? true)
</script>

<template>
  <div class="ps-wrap">
    <div v-if="loading" class="ps-msg">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
    </div>
    <div v-else-if="error" class="ps-msg ps-error">{{ error }}</div>
    <template v-else>
      <PhotoGridToolbar :count="photos.length">
        <template #left>
          <span style="font-weight:600;font-size:14px">{{ shareName }}</span>
        </template>
      </PhotoGridToolbar>
      <PhotoGrid
        :photos="photos"
        :columns="columns"
        style="flex:1;min-height:0"
        @photo-click="onPhotoClick"
      />
    </template>

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
  overflow: hidden;
  background: var(--el-bg-color-page);
  color: var(--el-text-color-primary);
  color-scheme: light dark;
}
.ps-wrap {
  position: fixed;
  inset: 0;
  display: flex;
  flex-direction: column;
  padding: 16px;
  max-width: 1200px;
  margin: 0 auto;
}
.ps-msg {
  flex: 1;
  display: flex; align-items: center; justify-content: center;
  font-size: 16px;
}
.ps-error { color: var(--el-color-danger); }
</style>
