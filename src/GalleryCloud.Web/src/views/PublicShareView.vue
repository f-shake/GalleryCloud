<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import PhotoGrid from '../components/PhotoGrid.vue'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoDetailView from './PhotoDetailView.vue'

const route = useRoute()
const token = route.params.token as string

const shareName = ref('')
const photos = ref<any[]>([])
const loading = ref(true)
const error = ref('')
const shareInfo = ref<any>(null)

const { columns } = usePhotoGrid()
const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

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
}
.ps-wrap {
  height: 100vh;
  display: flex;
  flex-direction: column;
}
.ps-msg {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
}
.ps-error { color: var(--el-color-danger); }
</style>
