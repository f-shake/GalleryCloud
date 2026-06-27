<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useInfiniteQuery } from '../composables/useInfiniteQuery'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'

const router = useRouter()
const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const { items: photos, loading, hasMore, loadMore } = useInfiniteQuery('/favorites')

onMounted(() => loadMore())

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height })
}

function onScroll(e: Event) {
  const el = e.target as HTMLElement
  if (el.scrollHeight - el.scrollTop - el.clientHeight < 500 && hasMore.value) loadMore()
}
</script>

<template>
  <div @scroll="onScroll" style="height:100%;overflow-y:auto;padding:16px">
    <div style="display:flex;align-items:center;gap:8px;margin-bottom:12px">
      <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ columns }}列</span>
      <div style="flex:1" />
      <el-button-group size="small">
        <el-button icon="Minus" @click="zoomOut" :disabled="columns >= 12" />
        <el-button icon="Plus" @click="zoomIn" :disabled="columns <= 3" />
      </el-button-group>
    </div>

    <el-empty v-if="!loading && photos.length === 0" description="暂无收藏" />

    <div v-else :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px' }">
      <div v-for="p in photos" :key="p.id"
        style="cursor:pointer;overflow:hidden;border-radius:4px;background:var(--el-fill-color-light);aspect-ratio:1"
        @click="onPhotoClick(p.id, $event)">
        <img :src="thumbUrl(p.id, 'grid', Math.ceil(400/columns*3))" loading="lazy"
          style="width:100%;height:100%;object-fit:cover;display:block" />
      </div>
    </div>

    <div v-if="loading" style="text-align:center;padding:24px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
  </div>
</template>
