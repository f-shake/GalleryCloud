<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import client from '../../api/client'
import { ElMessage } from 'element-plus'
import type { ScanStatus, ScanLog, ThumbnailGenerationStatus, ThumbnailStats } from '../../types'

const status = ref<ScanStatus>({ isRunning: false, mode: null, userId: null, startedAt: null, processedFiles: 0, totalFiles: 0, estimatedPercent: 0 })
const logs = ref<ScanLog[]>([])
const loading = ref(true)
const cancelling = ref(false)
const triggering = ref(false)

// Thumbnail state
const thumbStatus = ref<ThumbnailGenerationStatus>({ isRunning: false, processed: 0, total: 0, estimatedPercent: 0 })
const thumbStats = ref<ThumbnailStats>({ totalPhotos: 0, gridCached: 0, previewCached: 0, missingGrid: 0, missingPreview: 0 })
const thumbMode = ref<'rebuild' | 'fill' | null>(null)
const cancellingThumb = ref(false)
const thumbBusy = ref(false)

// Dialog state
const dialogVisible = ref(false)
const dialogMode = ref<'rebuild' | 'fill'>('fill')
const doGrid = ref(true)
const doPreview = ref(true)

let timer: any = null

onMounted(() => { loadLogs(); poll(); timer = setInterval(poll, 3000) })
onUnmounted(() => { if (timer) clearInterval(timer) })

const wasRunning = ref(false)
const thumbWasRunning = ref(false)

async function poll() {
  try { const r = await client.get('/admin/scan/status'); status.value = r.data }
  catch { /* */ }
  if (!status.value.isRunning) {
    if (wasRunning.value) loadLogs()
    cancelling.value = false; triggering.value = false
  }
  wasRunning.value = status.value.isRunning

  try {
    const [sr, tr] = await Promise.all([
      client.get('/admin/thumbnails/stats'),
      client.get('/admin/thumbnails/status'),
    ])
    thumbStats.value = sr.data
    thumbStatus.value = tr.data
  } catch { /* */ }
  if (!thumbStatus.value.isRunning && thumbWasRunning.value) {
    thumbBusy.value = false
    thumbMode.value = null
    cancellingThumb.value = false
  }
  thumbWasRunning.value = thumbStatus.value.isRunning
}

async function loadLogs() { loading.value = true; try { const r = await client.get('/admin/scan/logs?limit=20'); logs.value = r.data } catch { /* */ } finally { loading.value = false } }

async function triggerScan() {
  triggering.value = true
  try { await client.post('/admin/scan/trigger') }
  catch (e: any) {
    triggering.value = false
    ElMessage.warning(e.response?.data?.error || '操作失败')
  }
}
async function cancelScan() {
  cancelling.value = true
  try { await client.post('/admin/scan/cancel') }
  catch (e: any) {
    cancelling.value = false
    ElMessage.warning(e.response?.data?.error || '取消失败')
  }
}

// Block scan when thumbnail generation is running
const busy = computed(() => status.value.isRunning || cancelling.value || triggering.value || thumbStatus.value.isRunning)

// ── Thumbnail dialog ──
function openDialog(mode: 'rebuild' | 'fill') {
  dialogMode.value = mode
  doGrid.value = true
  doPreview.value = true
  dialogVisible.value = true
}

async function confirmDialog() {
  if (!doGrid.value && !doPreview.value) {
    ElMessage.warning('请至少选择一种缩略图')
    return
  }
  dialogVisible.value = false
  const sizes: string[] = []
  if (doGrid.value) sizes.push('grid')
  if (doPreview.value) sizes.push('preview')

  thumbMode.value = dialogMode.value
  thumbBusy.value = true
  const url = dialogMode.value === 'rebuild' ? '/admin/thumbnails/regenerate' : '/admin/thumbnails/fill-missing'
  const label = dialogMode.value === 'rebuild' ? '重建' : '补全'
  try {
    await client.post(url, { sizes })
    ElMessage.success(`缩略图${label}已启动`)
  } catch (e: any) {
    thumbMode.value = null
    thumbBusy.value = false
    ElMessage.error(e.response?.data?.error || '操作失败')
  }
}

async function cancelGeneration() {
  cancellingThumb.value = true
  try { await client.post('/admin/thumbnails/cancel') } catch { /* */ }
}

async function clearCache() {
  try {
    thumbBusy.value = true
    await client.delete('/admin/thumbnails/cache')
    ElMessage.success('缩略图缓存已清除')
    const sr = await client.get('/admin/thumbnails/stats')
    thumbStats.value = sr.data
  } catch (e: any) { ElMessage.error(e.response?.data?.error || '操作失败') }
  finally { thumbBusy.value = false }
}
</script>

<template>
  <div style="padding:16px">
    <!-- ==================== Scan ==================== -->
    <el-card style="margin-bottom:16px">
      <template #header>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <span>扫描状态</span>
          <div style="display:flex;gap:8px">
            <el-button type="primary" @click="triggerScan" :disabled="busy" :loading="status.isRunning">
              触发全量扫描
            </el-button>
            <el-button v-if="status.isRunning" type="danger" @click="cancelScan" :disabled="cancelling" :loading="cancelling">
              中断扫描
            </el-button>
          </div>
        </div>
      </template>
      <div v-if="status.isRunning" style="display:flex;gap:16px;margin-bottom:12px;flex-wrap:wrap;font-size:13px;color:var(--el-text-color-secondary)">
        <span>模式: <b>{{ { full: '全量', incremental: '增量', refreshexif: '刷新EXIF' }[status.mode!] || status.mode }}</b></span>
        <span>已处理: <b>{{ status.processedFiles >= 0 ? status.processedFiles.toLocaleString() : '枚举文件中...' }}</b><template v-if="status.totalFiles > 0"> / {{ status.totalFiles.toLocaleString() }}</template></span>
      </div>
      <el-progress v-if="status.isRunning" :percentage="status.estimatedPercent" :stroke-width="16" striped striped-flow />
      <div v-else style="color:var(--el-text-color-secondary);font-size:13px">空闲</div>
    </el-card>

    <!-- ==================== Scan History ==================== -->
    <el-card header="扫描历史">
      <el-table :data="logs" v-loading="loading" stripe size="small">
        <el-table-column prop="startedAt" label="开始时间" :formatter="(r:any) => r.startedAt?.replace('T',' ')?.substring(0,19)" />
        <el-table-column prop="mode" label="模式" width="80">
          <template #default="{ row }"><el-tag :type="row.mode === 'full' ? 'primary' : 'success'" size="small">{{ row.mode === 'full' ? '全量' : '增量' }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="totalFound" label="发现" width="80" />
        <el-table-column prop="newAdded" label="新增" width="80">
          <template #default="{ row }"><span style="color:var(--el-color-success)">+{{ row.newAdded }}</span></template>
        </el-table-column>
        <el-table-column prop="softDeleted" label="删除" width="80">
          <template #default="{ row }"><span style="color:var(--el-color-danger)">-{{ row.softDeleted }}</span></template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- ==================== Thumbnail Status ==================== -->
    <el-card style="margin-top:16px">
      <template #header>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <span>缩略图状态</span>
          <div style="display:flex;gap:8px">
            <el-button v-if="thumbStatus.isRunning" type="danger" @click="cancelGeneration" :disabled="cancellingThumb" :loading="cancellingThumb">
              中断生成
            </el-button>
            <el-button type="warning" @click="openDialog('rebuild')" :disabled="thumbBusy || busy" :loading="thumbStatus.isRunning && thumbMode === 'rebuild'">
              重建缩略图
            </el-button>
            <el-button type="primary" @click="openDialog('fill')" :disabled="thumbBusy || busy" :loading="thumbStatus.isRunning && thumbMode === 'fill'">
              补全缩略图
            </el-button>
            <el-button @click="clearCache" :disabled="thumbBusy || busy || thumbStatus.isRunning">清除缓存</el-button>
          </div>
        </div>
      </template>

      <!-- Stats -->
      <div style="display:flex;gap:20px;margin-bottom:12px;flex-wrap:wrap;font-size:13px;color:var(--el-text-color-secondary)">
        <span>总照片 <b style="color:var(--el-text-color-primary)">{{ thumbStats.totalPhotos.toLocaleString() }}</b></span>
        <span>网格 <b style="color:var(--el-text-color-primary)">{{ thumbStats.gridCached.toLocaleString() }}</b> / {{ thumbStats.totalPhotos.toLocaleString() }}</span>
        <span>预览 <b style="color:var(--el-text-color-primary)">{{ thumbStats.previewCached.toLocaleString() }}</b> / {{ thumbStats.totalPhotos.toLocaleString() }}</span>
      </div>

      <!-- Progress -->
      <div v-if="thumbStatus.isRunning" style="display:flex;gap:16px;margin-bottom:12px;flex-wrap:wrap;font-size:13px;color:var(--el-text-color-secondary)">
        <span>{{ thumbMode === 'rebuild' ? '全量重建' : '补全缺失' }}</span>
        <span>已处理 <b style="color:var(--el-text-color-primary)">{{ thumbStatus.processed.toLocaleString() }}</b> / {{ thumbStatus.total.toLocaleString() }}</span>
      </div>
      <el-progress v-if="thumbStatus.isRunning" :percentage="thumbStatus.estimatedPercent" :stroke-width="16" striped striped-flow />
      <div v-else style="color:var(--el-text-color-secondary);font-size:13px">
        <template v-if="thumbStats.totalPhotos === 0">等待扫描</template>
        <template v-else-if="thumbStats.missingGrid + thumbStats.missingPreview === 0">
          <span style="color:var(--el-color-success)">全部就绪 ✓</span>
        </template>
        <template v-else>
          还缺 <b style="color:var(--el-color-warning)">{{ (thumbStats.missingGrid + thumbStats.missingPreview).toLocaleString() }}</b> 个缩略图，点击「补全缩略图」生成
        </template>
      </div>
    </el-card>

    <!-- ==================== Dialog ==================== -->
    <el-dialog v-model="dialogVisible" :title="dialogMode === 'rebuild' ? '重建缩略图' : '补全缩略图'" width="360px" :close-on-click-modal="false">
      <div style="display:flex;flex-direction:column;gap:16px;padding:8px 0">
        <el-checkbox v-model="doGrid" :disabled="thumbBusy">网格缩略图 (400px)</el-checkbox>
        <el-checkbox v-model="doPreview" :disabled="thumbBusy">预览缩略图 (原图分辨率)</el-checkbox>
      </div>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmDialog">开始生成</el-button>
      </template>
    </el-dialog>
  </div>
</template>
