<script setup lang="ts">
import { ref } from 'vue'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoViewStore } from '../stores/photoViewStore'
import { useScanStatus } from '../composables/useScanStatus'

const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const { isScanning } = useScanStatus()
const photos = ref<any[]>([])
const total = ref(0)
const loading = ref(false)
const form = ref({ q: '', from: '', to: '', format: '', device: '' })

function onPhotoClick(id: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(id, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src)
}

async function search() {
  loading.value = true
  try {
    const params: any = {}
    for (const [k, v] of Object.entries(form.value)) if (v) params[k] = v
    const r = await client.get('/search', { params: { ...params, limit: 200 } })
    photos.value = r.data.photos
    total.value = r.data.total
  } catch { /* */ }
  finally { loading.value = false }
}
</script>

<template>
  <div style="padding:16px">
    <el-card style="margin-bottom:16px">
      <el-form :model="form" @submit.prevent="search" inline>
        <el-form-item><el-input v-model="form.q" placeholder="文件名" clearable style="width:180px" /></el-form-item>
        <el-form-item><el-date-picker v-model="form.from" type="date" placeholder="开始日期" style="width:140px" /></el-form-item>
        <el-form-item><el-date-picker v-model="form.to" type="date" placeholder="结束日期" style="width:140px" /></el-form-item>
        <el-form-item><el-input v-model="form.format" placeholder="格式 jpg,heic" style="width:140px" /></el-form-item>
        <el-form-item><el-input v-model="form.device" placeholder="设备型号" style="width:140px" /></el-form-item>
        <el-form-item><el-button type="primary" native-type="submit" :icon="'Search'">搜索</el-button></el-form-item>
      </el-form>
    </el-card>

    <div v-if="total > 0" style="display:flex;align-items:center;gap:8px;margin-bottom:12px">
      <el-tag type="info">找到 {{ total }} 张照片</el-tag>
      <div style="flex:1" />
      <el-button-group size="small">
        <el-button icon="Minus" @click="zoomOut" :disabled="columns >= 12" />
        <el-button icon="Plus" @click="zoomIn" :disabled="columns <= 3" />
      </el-button-group>
    </div>

    <div v-if="photos.length" :style="{ display:'grid', gridTemplateColumns:`repeat(${columns}, 1fr)`, gap:'0' }">
      <div v-for="p in photos" :key="p.id" class="thumb-cell" @click="onPhotoClick(p.id, $event)">
        <img v-lazy-img="thumbUrl(p.id, 'grid', 400)" class="thumb-img" />
      </div>
    </div>

    <el-empty v-if="!loading && photos.length === 0 && !isScanning" description="输入条件搜索" />
  </div>
</template>
