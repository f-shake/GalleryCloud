<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessage } from 'element-plus'
import client from '../../api/client'

const settings = ref<Record<string, string>>({})
const loading = ref(true)

const sections = [
  {
    name: 'scan', title: '扫描',
    fields: [
      { key: 'scan.cronExpression', label: 'Cron 表达式', hint: '定时扫描计划，默认每日凌晨3点', placeholder: '0 3 * * *' },
      { key: 'scan.supportedFormats', label: '文件格式', hint: '逗号分隔，含 . 前缀', placeholder: '.jpg,.jpeg,.heic' },
      { key: 'scan.excludePatterns', label: '排除路径', hint: '逗号分隔的 glob 模式', placeholder: '**/thumbnails/**' },
    ],
  },
  {
    name: 'watcher', title: '文件监控',
    fields: [
      { key: 'filewatcher.enabled', label: '启用监控', type: 'switch', hint: '监控文件系统变更，自动增量更新' },
      { key: 'filewatcher.debounceDelayMs', label: '防抖延迟 (ms)', hint: '文件变更后的合并等待时间', placeholder: '5000' },
    ],
  },
  {
    name: 'thumbnail', title: '缩略图',
    fields: [
      { key: 'thumbnail.format', label: '编码格式', type: 'select', options: [{ v: 'webp', l: 'WebP' }, { v: 'jpg', l: 'JPG' }], hint: 'WebP 体积更小，JPG 兼容性更好' },
      { key: 'thumbnail.quality', label: '质量', type: 'number', hint: '10-100，数值越高文件越大', placeholder: '80' },
      { key: 'thumbnail.parallelThreads', label: '并行线程', type: 'number', hint: '1 ~ CPU 核心数，默认 2', placeholder: '2' },
      { key: 'thumbnail.cacheDir', label: '缓存目录', hint: '缩略图磁盘缓存路径', placeholder: 'data/thumbnails' },
      { key: 'thumbnail.maxMemoryCacheMb', label: '内存缓存 (MB)', type: 'number', hint: '最近访问的缩略图内存缓存上限', placeholder: '512' },
    ],
  },
  {
    name: 'preview', title: '预览',
    fields: [
      { key: 'preview.format', label: '编码格式', type: 'select', options: [{ v: 'webp', l: 'WebP' }, { v: 'jpg', l: 'JPG' }], hint: '查看大图时的格式' },
      { key: 'preview.quality', label: '质量', type: 'number', hint: '10-100', placeholder: '85' },
      { key: 'preview.maxResolution', label: '最大分辨率 (px)', type: 'number', hint: '任意一边不超过此值', placeholder: '2560' },
    ],
  },
  {
    name: 'map', title: '地图',
    fields: [
      { key: 'map.defaultBasemap', label: '默认底图', type: 'select', options: [{ v: 'normal', l: '普通' }, { v: 'satellite', l: '卫星' }], hint: '地图页面的初始底图类型' },
      { key: 'map.tileUrlNormal', label: '普通底图 XYZ URL', hint: '需支持 {z}/{x}/{y} 占位符' },
      { key: 'map.tileUrlSatellite', label: '卫星底图 XYZ URL', hint: '需支持 {z}/{x}/{y} 占位符' },
    ],
  },
  {
    name: 'auth', title: '认证',
    fields: [
      { key: 'auth.tokenExpiryDays', label: 'Token 有效期 (天)', type: 'number', hint: '登录后多少天需要重新登录', placeholder: '30' },
    ],
  },
]

const activeTab = ref('scan')

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
  <div style="padding:16px;max-width:720px">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h3 style="margin:0">系统设置</h3>
      <el-button type="primary" @click="save">保存全部</el-button>
    </div>

    <el-skeleton :loading="loading" animated :count="3">
      <el-tabs v-model="activeTab" type="border-card">
        <el-tab-pane v-for="s in sections" :key="s.name" :name="s.name" :label="s.title">
          <el-form label-width="150px" label-position="left">
            <el-form-item v-for="f in s.fields" :key="f.key" :label="f.label">
              <!-- Switch -->
              <el-switch
                v-if="f.type === 'switch'"
                :model-value="settings[f.key] === 'true'"
                @update:model-value="settings[f.key] = $event ? 'true' : 'false'"
              />
              <!-- Select -->
              <el-select
                v-else-if="f.type === 'select'"
                v-model="settings[f.key]"
                style="width:200px"
              >
                <el-option v-for="o in f.options" :key="o.v" :label="o.l" :value="o.v" />
              </el-select>
              <!-- Number -->
              <el-input-number
                v-else-if="f.type === 'number'"
                v-model="settings[f.key]"
                style="width:200px"
                :min="f.key === 'thumbnail.quality' ? 10 : 1"
                :max="f.key === 'thumbnail.quality' ? 100 : f.key === 'thumbnail.maxMemoryCacheMb' ? 32768 : undefined"
              />
              <!-- Default: text input -->
              <el-input v-else v-model="settings[f.key]" :placeholder="f.placeholder" />
              <!-- Hint -->
              <div v-if="f.hint" style="font-size:12px;color:var(--el-text-color-secondary);margin-top:4px">{{ f.hint }}</div>
            </el-form-item>
          </el-form>
        </el-tab-pane>
      </el-tabs>
    </el-skeleton>
  </div>
</template>
