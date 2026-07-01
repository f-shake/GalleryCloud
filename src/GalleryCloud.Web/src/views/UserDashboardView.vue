<script setup lang="ts">
import { ref, onMounted } from 'vue'
import client from '../api/client'

interface Stats { totalPhotos: number; totalSizeGb: number; photosWithGps: number; formatDistribution: { format: string; count: number }[] }
const stats = ref<Stats | null>(null)
const loading = ref(true)

onMounted(async () => {
  try { const r = await client.get('/user/stats'); stats.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
})
</script>

<template>
  <div style="padding:16px">
    <el-skeleton :loading="loading" animated :count="1">
      <div v-if="stats" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:16px;margin-bottom:24px">
        <el-statistic title="我的照片" :value="stats.totalPhotos" />
        <el-statistic title="存储空间" :value="stats.totalSizeGb" suffix="GB" :precision="2" />
        <el-statistic title="含 GPS 照片" :value="stats.photosWithGps" />
      </div>
    </el-skeleton>

    <el-card v-if="stats" header="格式分布" style="margin-top:16px">
      <div style="display:flex;gap:12px;flex-wrap:wrap">
        <el-tag v-for="f in stats.formatDistribution" :key="f.format" type="info" effect="plain">
          {{ f.format }}: {{ f.count }}
        </el-tag>
      </div>
    </el-card>
  </div>
</template>
