<script setup lang="ts">
import { ref, onMounted } from 'vue'
import client from '../api/client'
import { useScanStatus } from '../composables/useScanStatus'
import { thumbUrl } from '../composables/useThumbnailUrl'

interface Cluster { lat: number; lng: number; count: number; photos: any[] }

const clusters = ref<Cluster[]>([])
const basemap = ref<'normal'|'satellite'>('normal')
const tileUrls = ref({ normal: '', satellite: '' })
const loading = ref(true)

onMounted(async () => {
  try {
    const [cfg, cl] = await Promise.all([
      client.get('/map/basemap-config'),
      client.get('/map/clusters?zoom=10'),
    ])
    tileUrls.value = { normal: cfg.data.tileUrlNormal, satellite: cfg.data.tileUrlSatellite }
    basemap.value = cfg.data.defaultBasemap
    clusters.value = cl.data.clusters
  } catch { /* */ }
  finally { loading.value = false }
})
</script>

<template>
  <div style="height:100%;display:flex;flex-direction:column;padding:16px">
    <div style="display:flex;align-items:center;gap:12px;margin-bottom:16px">
      <span style="font-size:13px;color:var(--el-text-color-secondary)">{{ clusters.length }} 个位置点</span>
      <div style="flex:1" />
      <el-radio-group v-model="basemap" size="small">
        <el-radio-button value="normal">普通</el-radio-button>
        <el-radio-button value="satellite">卫星</el-radio-button>
      </el-radio-group>
    </div>

    <el-empty v-if="!loading && clusters.length === 0" description="暂无含 GPS 位置的照片" />

    <div v-else style="flex:1;overflow-y:auto;display:grid;grid-template-columns:repeat(auto-fill, minmax(180px, 1fr));gap:12px;align-content:start">
      <el-card v-for="(c, i) in clusters" :key="i" shadow="hover" body-style="padding:12px">
        <div style="text-align:center">
          <div style="font-size:28px;font-weight:700;color:var(--el-color-primary)">{{ c.count }}</div>
          <div style="font-size:12px;color:var(--el-text-color-secondary);margin:4px 0">{{ c.lat.toFixed(4) }}, {{ c.lng.toFixed(4) }}</div>
          <div style="display:flex;gap:2px;flex-wrap:wrap;justify-content:center;margin-top:8px">
            <el-image v-for="p in c.photos.slice(0, 6)" :key="p.id"
              :src="thumbUrl(p.id, 'grid', 60)"
              style="width:48px;height:48px;border-radius:4px" fit="cover" lazy />
          </div>
        </div>
      </el-card>
    </div>
  </div>
</template>
