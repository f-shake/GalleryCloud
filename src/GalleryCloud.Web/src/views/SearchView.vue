<script setup lang="ts">
import { ref } from 'vue'
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
const form = ref({ q: '', from: '', to: '', format: '', device: '' })

const { onPhotoClick } = usePhotoClick(() => toNavItems(photos.value))

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
</script>

<template>
  <div class="sr-wrap">
    <div class="sr-form">
      <el-card>
        <el-form :model="form" @submit.prevent="search" inline>
          <el-form-item><el-input v-model="form.q" placeholder="文件名" clearable style="width:180px" /></el-form-item>
          <el-form-item><el-date-picker v-model="form.from" type="date" placeholder="开始日期" style="width:140px" /></el-form-item>
          <el-form-item><el-date-picker v-model="form.to" type="date" placeholder="结束日期" style="width:140px" /></el-form-item>
          <el-form-item><el-input v-model="form.format" placeholder="格式 jpg,heic" style="width:140px" /></el-form-item>
          <el-form-item><el-input v-model="form.device" placeholder="设备型号" style="width:140px" /></el-form-item>
          <el-form-item><el-button type="primary" native-type="submit" :icon="'Search'">搜索</el-button></el-form-item>
        </el-form>
      </el-card>
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
.sr-form { flex-shrink: 0; }
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
