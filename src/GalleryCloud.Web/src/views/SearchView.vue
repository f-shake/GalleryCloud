<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import client from '../api/client'
import { useScanStatus } from '../composables/useScanStatus'
import { usePhotoClick, toNavItems } from '../composables/usePhotoClick'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'
import PhotoGrid from '../components/PhotoGrid.vue'

const { columns } = usePhotoGrid()
const { isScanning } = useScanStatus()
const photos = ref<any[]>([])
const total = ref(0)
const loading = ref(false)

interface FilterOptions { formats: string[]; deviceModels: string[]; tags: { id: string; name: string; color: string | null }[] }
const filters = ref<FilterOptions>({ formats: [], deviceModels: [], tags: [] })

const form = ref({ q: '', from: '', to: '', format: '', device: '', tag: '' })

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

onMounted(async () => {
  try {
    const r = await client.get('/search/filters')
    filters.value = r.data
  } catch { /* */ }
})

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
      <el-form :model="form" @submit.prevent="search" class="sr-form-inner">
        <div class="sr-form-row">
          <el-form-item>
            <el-input v-model="form.q" placeholder="文件名" clearable style="width:180px" @keyup.enter="search" />
          </el-form-item>
          <el-form-item>
            <el-date-picker v-model="form.from" type="date" placeholder="开始日期" value-format="YYYY-MM-DD" style="width:150px" />
          </el-form-item>
          <el-form-item>
            <el-date-picker v-model="form.to" type="date" placeholder="结束日期" value-format="YYYY-MM-DD" style="width:150px" />
          </el-form-item>
          <el-form-item>
            <el-select v-model="form.format" placeholder="格式" clearable style="width:130px">
              <el-option v-for="f in filters.formats" :key="f" :label="f.toUpperCase()" :value="f" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-select v-model="form.device" placeholder="设备型号" clearable filterable style="width:180px">
              <el-option v-for="d in filters.deviceModels" :key="d" :label="d" :value="d" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-select v-model="form.tag" placeholder="标签" clearable style="width:140px">
              <el-option v-for="t in filters.tags" :key="t.id" :label="t.name" :value="t.name">
                <span style="display:flex;align-items:center;gap:6px">
                  <span v-if="t.color" :style="{ display:'inline-block', width:8, height:8, borderRadius:'50%', background:t.color }" />
                  {{ t.name }}
                </span>
              </el-option>
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" native-type="submit" :icon="'Search'">搜索</el-button>
          </el-form-item>
        </div>
      </el-form>
    </div>

    <div v-if="total > 0" class="sr-toolbar">
      <PhotoGridToolbar :count="total" />
    </div>

    <PhotoGrid v-if="photos.length" :photos="photos" :columns="columns" @photo-click="onPhotoClick" style="flex:1;min-height:0" />

    <div v-if="loading && photos.length === 0" class="sr-state-overlay"><el-icon class="is-loading" :size="24"><Loading /></el-icon></div>
    <el-empty v-else-if="!loading && photos.length === 0 && !isScanning" description="输入条件搜索" />
  </div>
</template>

<style>
.sr-wrap { position: absolute; inset: 0; display: flex; flex-direction: column; }
.sr-form { flex-shrink: 0; border-bottom: 1px solid var(--el-border-color-light); background: var(--el-bg-color-overlay); }
.sr-form-inner { padding: 10px 16px; }
.sr-form-row { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
.sr-form-row .el-form-item { margin-bottom: 0; }
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
