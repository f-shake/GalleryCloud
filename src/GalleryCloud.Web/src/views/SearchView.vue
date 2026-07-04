<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import client from '../api/client'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'
import MapAreaPicker from '../components/MapAreaPicker.vue'

const { columns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const photos = ref<any[]>([])
const total = ref(0)
const loading = ref(false)

interface TagInfo { id: string; name: string; color: string | null }
interface FilterOptions { formats: string[]; deviceModels: string[]; tags: TagInfo[] }
const filters = ref<FilterOptions>({ formats: [], deviceModels: [], tags: [] })

const form = ref({ q: '', from: '', to: '', format: '', device: '', tag: '', lat1: '', lng1: '', lat2: '', lng2: '' })
const showMapPicker = ref(false)

const hasBbox = ref(false)

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

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

function onMapConfirm(bounds: { south: number; north: number; west: number; east: number } | null) {
  if (bounds) {
    form.value.lat1 = String(bounds.south)
    form.value.lng1 = String(bounds.west)
    form.value.lat2 = String(bounds.north)
    form.value.lng2 = String(bounds.east)
    hasBbox.value = true
    search()
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
  search()
}

async function search() {
  loading.value = true
  try {
    const params: any = {}
    for (const [k, v] of Object.entries(form.value)) if (v) params[k] = v
    const r = await client.get('/search', { params: { ...params } })
    photos.value = r.data.photos
    total.value = r.data.total
  } catch { /* */ }
  finally { loading.value = false }
}

function onFormatChange(val: string | string[]) {
  form.value.format = Array.isArray(val) ? val.join(',') : val
}
</script>

<template>
  <div class="sr-wrap">
    <div class="sr-form">
      <div class="sr-form-inner">
        <div class="sr-filters-row">
          <div class="sr-field">
            <span class="sr-label">文件名</span>
            <el-input v-model="form.q" placeholder="搜索..." clearable style="width:160px" @keyup.enter="search" />
          </div>
          <div class="sr-field">
            <span class="sr-label">日期</span>
            <div style="display:flex;align-items:center;gap:4px">
              <el-date-picker v-model="form.from" type="date" placeholder="开始" value-format="YYYY-MM-DD" style="width:130px" />
              <span style="color:var(--el-text-color-secondary)">—</span>
              <el-date-picker v-model="form.to" type="date" placeholder="结束" value-format="YYYY-MM-DD" style="width:130px" />
            </div>
          </div>
          <div class="sr-field">
            <span class="sr-label">格式</span>
            <el-select v-model="form.format" placeholder="全部" clearable style="width:110px" @change="search">
              <el-option v-for="f in filters.formats" :key="f" :label="f.toUpperCase()" :value="f" />
            </el-select>
          </div>
          <div class="sr-field">
            <span class="sr-label">设备</span>
            <el-select v-model="form.device" placeholder="全部" clearable filterable style="width:170px" @change="search">
              <el-option v-for="d in filters.deviceModels" :key="d" :label="d" :value="d" />
            </el-select>
          </div>
          <div class="sr-field">
            <span class="sr-label">区域</span>
            <div style="display:flex;gap:4px">
              <el-button v-if="!hasBbox" size="default" @click="openMapPicker" style="white-space:nowrap">
                <el-icon style="margin-right:3px"><MapLocation /></el-icon>地图
              </el-button>
              <el-tag v-else closable type="info" size="default" style="cursor:pointer;font-size:13px;padding:0 10px;height:32px;line-height:30px" @click="openMapPicker" @close="clearBbox">
                <el-icon style="margin-right:3px"><MapLocation /></el-icon>已选区域
              </el-tag>
            </div>
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
      <PhotoGridToolbar :count="total" />
    </div>

    <PhotoGrid v-if="photos.length" :photos="photos" :columns="columns" @photo-click="onPhotoClick" style="flex:1;min-height:0" />

    <div v-if="loading && photos.length === 0" class="sr-state-overlay"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
    <el-empty v-else-if="!loading && photos.length === 0 && !isScanning" description="输入条件搜索" />

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
.sr-filters-row { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; }
.sr-field { display: flex; flex-direction: column; gap: 2px; }
.sr-field-btn { justify-content: flex-end; }
.sr-label { font-size: 11px; color: var(--el-text-color-secondary); white-space: nowrap; }
.sr-tags-row { display: flex; align-items: center; gap: 8px; }
.sr-tags-list { display: flex; flex-wrap: wrap; gap: 6px; }
.sr-tag--active { background: var(--el-color-primary-light-9) !important; border-color: var(--el-color-primary) !important; color: var(--el-color-primary) !important; font-weight: 600; }
.sr-toolbar {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 8px;
  padding: 4px 16px;
  background: var(--el-bg-color-page);
}
.sr-state-overlay {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  z-index: 5; pointer-events: none;
}
</style>
