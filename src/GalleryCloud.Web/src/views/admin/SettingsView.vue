<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import client from '../../api/client'

const settings = ref<Record<string, string>>({})
const loading = ref(true)
const cpuCores = navigator.hardwareConcurrency || 4

interface FieldConfig {
  key: string; label: string; hint?: string
  type?: 'switch' | 'number' | 'select' | 'slider'
  placeholder?: string
  options?: { v: string; l: string }[]
}

const sections: { title: string; fields: FieldConfig[] }[] = [
  {
    title: '扫描',
    fields: [
      { key: 'scan.cronExpression', label: 'Cron 表达式', hint: '定时扫描计划，默认每日凌晨3点', placeholder: '0 3 * * *' },
      { key: 'scan.supportedFormats', label: '文件格式', hint: '逗号分隔，含 . 前缀', placeholder: '.jpg,.jpeg,.heic' },
      { key: 'scan.excludePatterns', label: '排除路径', hint: '逗号分隔的 glob 模式', placeholder: '**/thumbnails/**' },
    ],
  },
  {
    title: '文件监控',
    fields: [
      { key: 'filewatcher.enabled', label: '启用监控', type: 'switch', hint: '监控文件系统变更，自动增量更新' },
      { key: 'filewatcher.debounceDelayMs', label: '防抖延迟 (ms)', hint: '文件变更后的合并等待时间', placeholder: '5000' },
    ],
  },
  {
    title: '图片处理',
    fields: [
      { key: 'image.processingEngine', label: '处理引擎', type: 'select', options: [{ v: 'ImageSharp', l: 'ImageSharp' }, { v: 'MagickNET', l: 'Magick.NET' }] },
    ],
  },
  {
    title: '缩略图',
    fields: [
      { key: 'thumbnail.format', label: '格式', type: 'select', options: [{ v: 'jpeg', l: 'JPEG' }, { v: 'webp', l: 'WebP' }], hint: '切换后已有缩略图仍可用，新生成使用新格式' },
      { key: 'thumbnail.quality', label: '质量', type: 'slider', hint: '10-100，数值越高文件越大' },
      { key: 'thumbnail.parallelThreads', label: '并行线程', type: 'number', hint: `1 ~ ${cpuCores}（CPU 线程数）`, placeholder: '4' },
      { key: 'thumbnail.maxMemoryCacheMb', label: '内存缓存 (MB)', type: 'number', placeholder: '512' },
    ],
  },
  {
    title: '预览',
    fields: [
      { key: 'preview.format', label: '格式', type: 'select', options: [{ v: 'jpeg', l: 'JPEG' }, { v: 'webp', l: 'WebP' }], hint: '切换后已有预览仍可用，新生成使用新格式' },
      { key: 'preview.quality', label: '质量', type: 'slider', hint: '10-100，数值越高文件越大' },
      { key: 'preview.maxResolution', label: '最大分辨率 (px)', type: 'number', hint: '任意一边不超过此值', placeholder: '5000' },
    ],
  },
  {
    title: '地图',
    fields: [
      { key: 'map.tileUrlNormal', label: '普通底图 XYZ URL', hint: '需支持 {z}/{x}/{y} 占位符' },
      { key: 'map.tileUrlSatellite', label: '卫星底图 XYZ URL', hint: '需支持 {z}/{x}/{y} 占位符' },
    ],
  },
]

onMounted(async () => {
  try { const r = await client.get('/admin/settings'); settings.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
})

async function save() {
  try {
    await client.put('/admin/settings', settings.value)
    ElMessage.success('已保存')
  } catch { ElMessage.error('保存失败') }
}
</script>

<template>
  <div style="height:100%;overflow-y:auto">
    <el-skeleton :loading="loading" animated :count="3">
      <div style="max-width:720px;margin:0 auto;padding:16px;display:flex;flex-direction:column;gap:16px">

        <el-card v-for="s in sections" :key="s.title">
          <template #header>
            <span style="font-weight:600;font-size:14px">{{ s.title }}</span>
          </template>
          <el-form label-width="140px" label-position="left">
            <el-form-item v-for="f in s.fields" :key="f.key" :label="f.label">
              <div v-if="f.type === 'switch'">
                <el-switch
                  :model-value="settings[f.key] === 'true'"
                  @update:model-value="settings[f.key] = $event ? 'true' : 'false'"
                />
                <div v-if="f.hint" class="field-hint">{{ f.hint }}</div>
              </div>
              <el-select v-else-if="f.type === 'select'" v-model="settings[f.key]" style="width:200px">
                <el-option v-for="o in f.options" :key="o.v" :label="o.l" :value="o.v" />
              </el-select>
              <div v-else-if="f.type === 'slider'" style="display:flex;align-items:center;gap:12px;width:100%">
                <el-slider
                  :model-value="Number(settings[f.key]) || (f.key.includes('preview') ? 70 : 60)"
                  @update:model-value="settings[f.key] = String($event)"
                  :min="10" :max="100" style="flex:1"
                />
                <span style="min-width:32px;text-align:right;font-size:13px;color:var(--el-text-color-secondary)">{{ settings[f.key] || (f.key.includes('preview') ? '70' : '60') }}</span>
              </div>
              <el-input-number
                v-else-if="f.type === 'number'"
                :model-value="Number(settings[f.key]) || 0"
                @update:model-value="settings[f.key] = String($event)"
                :min="f.key.includes('quality') ? 10 : 1"
                :max="f.key === 'thumbnail.parallelThreads' ? cpuCores : (f.key.includes('quality') ? 100 : 32768)"
              />
              <el-input v-else v-model="settings[f.key]" :placeholder="f.placeholder" />
              <div v-if="f.hint && f.type !== 'switch'" class="field-hint">{{ f.hint }}</div>
            </el-form-item>
          </el-form>
        </el-card>

        <div style="text-align:center;padding:8px 0 32px">
          <el-button type="primary" @click="save" size="large" style="min-width:160px">保存全部</el-button>
        </div>
      </div>
    </el-skeleton>
  </div>
</template>

<style scoped>
.field-hint {
  flex-basis: 100%;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-top: 4px;
  line-height: 1.5;
}
:deep(.el-form-item__content) { flex-wrap: wrap; }
:deep(.el-form-item) { margin-bottom: 14px; }
</style>
