<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { useSelectionStore } from '../stores/selectionStore'
import client from '../api/client'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import { useLongPressSelection } from '../composables/useLongPressSelection'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'
import BatchToolbar from '../components/BatchToolbar.vue'
import MapAreaPicker from '../components/MapAreaPicker.vue'

const { columns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const selStore = useSelectionStore()
const photos = ref<any[]>([])
const total = ref(0)
const loading = ref(false)

interface TagInfo { id: string; name: string; color: string | null }
interface FilterOptions { formats: string[]; deviceModels: string[]; tags: TagInfo[] }
const filters = ref<FilterOptions>({ formats: [], deviceModels: [], tags: [] })

const form = ref({ q: '', from: '', to: '', format: '', device: '', tag: '', lat1: '', lng1: '', lat2: '', lng2: '' })
const showMapPicker = ref(false)

const hasBbox = ref(false)
const bboxArea = ref('')

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))
const { onTouchStart, onTouchMove, onTouchEnd } = useLongPressSelection()

onMounted(async () => {
  try {
    const r = await client.get('/search/filters')
    filters.value = r.data
  } catch { /* */ }
})

function toggleTag(name: string) {
  form.value.tag = form.value.tag === name ? '' : name
  search()
}

function openMapPicker() {
  showMapPicker.value = true
}

interface BboxResult { south: number; north: number; west: number; east: number; areaKm2?: number }
function onMapConfirm(bounds: BboxResult | null) {
  if (bounds) {
    form.value.lat1 = String(bounds.south)
    form.value.lng1 = String(bounds.west)
    form.value.lat2 = String(bounds.north)
    form.value.lng2 = String(bounds.east)
    hasBbox.value = true
    bboxArea.value = bounds.areaKm2 != null ? String(bounds.areaKm2) : ''
  } else {
    clearBbox()
  }
}

function clearBbox() {
  form.value.lat1 = ''
  form.value.lng1 = ''
  form.value.lat2 = ''
  form.value.lng2 = ''
  hasBbox.value = false
  bboxArea.value = ''
  search()
}

async function search() {
  selStore.disable()
  loading.value = true
  try {
    const params: any = { limit: 1000000 }
    for (const [k, v] of Object.entries(form.value)) if (v) params[k] = v
    const r = await client.get('/search', { params: { ...params } })
    photos.value = r.data.photos
    total.value = r.data.total
  } catch { /* */ }
  finally { loading.value = false }
}

watch(photos, (val) => {
  selStore.setViewPhotos(val.map((p: any) => ({ id: p.id, takenAt: p.takenAt })))
}, { immediate: true })

</script>

<template>
  <div class="sr-wrap">
    <div class="sr-form">
      <div class="sr-form-inner">
        <div class="sr-filters">
          <div class="sr-field">
            <span class="sr-label">文件名</span>
            <el-input v-model="form.q" placeholder="搜索..." clearable @keyup.enter="search" />
          </div>
          <div class="sr-field">
            <span class="sr-label">日期</span>
            <div class="sr-date-range">
              <el-date-picker v-model="form.from" type="date" placeholder="开始" value-format="YYYY-MM-DD" />
              <span class="sr-date-sep">—</span>
              <el-date-picker v-model="form.to" type="date" placeholder="结束" value-format="YYYY-MM-DD" />
            </div>
          </div>
          <div class="sr-field">
            <span class="sr-label">格式</span>
            <el-select v-model="form.format" placeholder="全部" clearable>
              <el-option v-for="f in filters.formats" :key="f" :label="f.toUpperCase()" :value="f" />
            </el-select>
          </div>
          <div class="sr-field">
            <span class="sr-label">设备</span>
            <el-select v-model="form.device" placeholder="全部" clearable filterable>
              <el-option v-for="d in filters.deviceModels" :key="d" :label="d" :value="d" />
            </el-select>
          </div>
          <div class="sr-field">
            <span class="sr-label">区域</span>
            <el-button v-if="!hasBbox" @click="openMapPicker" style="white-space:nowrap">
              <el-icon style="margin-right:3px"><MapLocation /></el-icon>地图
            </el-button>
            <el-tag v-else closable type="info" size="default" style="cursor:pointer;font-size:13px;padding:0 10px;height:32px;line-height:30px" @click="openMapPicker" @close="clearBbox">
              <el-icon style="margin-right:3px"><MapLocation /></el-icon>{{ bboxArea }} km²
            </el-tag>
          </div>
          <div class="sr-field sr-field-btn">
            <el-button type="primary" :icon="'Search'" @click="search">搜索</el-button>
          </div>
        </div>

        <div v-if="filters.tags.length" class="sr-tags-row">
          <span class="sr-label">标签</span>
          <div class="sr-tags-list">
            <el-tag
              v-for="t in filters.tags"
              :key="t.id"
              :hit="form.tag === t.name"
              :class="{ 'sr-tag--active': form.tag === t.name }"
              :style="t.color && form.tag !== t.name ? { borderColor: t.color, color: t.color } : undefined"
              style="cursor:pointer;transition:all .2s"
              @click="toggleTag(t.name)"
            >
              <span v-if="t.color" :style="{ display:'inline-block', width:6, height:6, borderRadius:'50%', background: t.color, marginRight: 4 }" />
              {{ t.name }}
            </el-tag>
          </div>
        </div>
      </div>
    </div>

    <div v-if="photos.length || total > 0" class="sr-toolbar">
      <PhotoGridToolbar :count="total" selectionSource="search" @batch-hide="search()" />
    </div>

    <div class="sr-results" @touchstart="onTouchStart($event, '')" @touchmove="onTouchMove" @touchend="onTouchEnd">
      <PhotoGrid
        v-if="photos.length"
        :photos="photos"
        :columns="columns"
        :selection-mode="selStore.enabled"
        :selected-ids="selStore.selectedIds"
        @photo-click="onPhotoClick"
        @selection-toggle="selStore.toggle"
        style="flex:1;min-height:0"
      />
      <div v-if="loading" class="sr-loading-overlay"><el-icon class="is-loading" :size="28"><Loading /></el-icon></div>
      <el-empty v-else-if="!loading && photos.length === 0 && !isScanning" description="输入条件搜索" />
    </div>

    <MapAreaPicker
      v-model="showMapPicker"
      :south="form.lat1 ? Number(form.lat1) : undefined"
      :north="form.lat2 ? Number(form.lat2) : undefined"
      :west="form.lng1 ? Number(form.lng1) : undefined"
      :east="form.lng2 ? Number(form.lng2) : undefined"
      @confirm="onMapConfirm"
    />
  </div>
</template>

<style>
.sr-wrap { position: absolute; inset: 0; display: flex; flex-direction: column; }
.sr-form { flex-shrink: 0; border-bottom: 1px solid var(--el-border-color-light); background: var(--el-bg-color-overlay); }
.sr-form-inner { padding: 8px 16px; display: flex; flex-direction: column; gap: 8px; }
.sr-filters { display: grid; grid-template-columns: repeat(6, auto); align-items: end; gap: 8px 12px; }
.sr-field { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.sr-field .el-input,
.sr-field .el-select { width: 140px; }
.sr-field-btn { justify-self: end; }
.sr-date-range { display: flex; align-items: center; gap: 4px; }
.sr-date-range .el-date-editor { width: 120px; }
.sr-date-sep { color: var(--el-text-color-secondary); flex-shrink: 0; }
.sr-field-btn .el-button { white-space: nowrap; }
.sr-label { font-size: 11px; color: var(--el-text-color-secondary); white-space: nowrap; }
.sr-tags-row { display: flex; align-items: center; gap: 8px; }

@media (max-width: 1079px) {
  .sr-filters { grid-template-columns: repeat(3, 1fr); }
  .sr-field .el-input,
  .sr-field .el-select { width: 100%; }
  .sr-date-range .el-date-editor { width: 100%; }
  .sr-field-btn { justify-self: stretch; }
}
@media (max-width: 639px) {
  .sr-filters { grid-template-columns: 1fr 1fr; }
  .sr-field:nth-child(1),
  .sr-field:nth-child(2) { grid-column: 1 / -1; }
}
.sr-tags-list { display: flex; flex-wrap: wrap; gap: 6px; }
.sr-tag--active { background: var(--el-color-primary-light-9) !important; border-color: var(--el-color-primary) !important; color: var(--el-color-primary) !important; font-weight: 600; }
.sr-toolbar {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 8px;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
}
.sr-results { flex: 1; min-height: 0; position: relative; display: flex; flex-direction: column; }
.sr-loading-overlay {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  z-index: 5; pointer-events: none;
  background: var(--el-bg-color-page);
  opacity: 0.7;
}
</style>
