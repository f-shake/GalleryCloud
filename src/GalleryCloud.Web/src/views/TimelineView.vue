<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useTimeline } from '../composables/useTimeline'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

const router = useRouter()
const viewStore = usePhotoViewStore()
const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
const { groups, loading, hasMore, loadMore } = useTimeline(groupLevel)

onMounted(() => loadMore())

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height })
}

let scrollEl: HTMLElement | null = null
onMounted(() => {
  scrollEl = document.querySelector('.app-main')
  scrollEl?.addEventListener('scroll', onScroll, { passive: true })
})
onUnmounted(() => {
  scrollEl?.removeEventListener('scroll', onScroll)
})

function onScroll() {
  if (!scrollEl) return
  if (scrollEl.scrollHeight - scrollEl.scrollTop - scrollEl.clientHeight < 500 && hasMore.value) loadMore()
}
</script>

<template>
  <div style="padding:16px">
    <div style="display:flex;align-items:center;gap:8px;margin-bottom:16px">
      <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ columns }}列 · {{ groupLevel === 'day' ? '按天' : groupLevel === 'month' ? '按月' : '平铺' }}</span>
      <div style="flex:1" />
      <el-button-group size="small">
        <el-button :icon="'Minus'" @click="zoomOut" :disabled="columns >= 12" />
        <el-button :icon="'Plus'" @click="zoomIn" :disabled="columns <= 3" />
      </el-button-group>
    </div>

    <el-empty v-if="groups.length === 0 && !loading && !isScanning" description="暂无照片" />

    <div v-for="(group, i) in groups" :key="i" style="margin-bottom:24px">
      <div v-if="group.label" style="position:sticky;top:0;z-index:10;background:var(--el-bg-color-page);padding:8px 0;margin-bottom:8px">
        <el-tag type="info" size="large">{{ group.label }}</el-tag>
      </div>
      <div :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'4px' }">
        <div v-for="p in group.photos" :key="p.id"
          class="thumb-cell"
          @click="onPhotoClick(p.id, $event)">
          <img
            v-lazy-img="thumbUrl(p.id, 'grid', Math.ceil(400/columns*3))"
            class="thumb-img"
          />
        </div>
      </div>
    </div>

    <div v-if="loading" style="text-align:center;padding:24px"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
  </div>
</template>
