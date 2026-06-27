<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import client from '../../api/client'
import { ElMessage } from 'element-plus'
import type { ScanStatus, ScanLog } from '../../types'

const status = ref<ScanStatus>({ isRunning: false, mode: null, userId: null, startedAt: null, processedFiles: 0, totalFiles: 0, estimatedPercent: 0 })
const logs = ref<ScanLog[]>([])
const loading = ref(true)
const cancelling = ref(false)
const triggering = ref(false)
let timer: any = null

onMounted(() => { loadLogs(); poll(); timer = setInterval(poll, 3000) })
onUnmounted(() => { if (timer) clearInterval(timer) })

const wasRunning = ref(false)

async function poll() {
  try { const r = await client.get('/admin/scan/status'); status.value = r.data }
  catch { /* */ }
  if (!status.value.isRunning) {
    if (wasRunning.value) loadLogs()  // scan just finished
    cancelling.value = false; triggering.value = false
  }
  wasRunning.value = status.value.isRunning
}
async function loadLogs() { loading.value = true; try { const r = await client.get('/admin/scan/logs?limit=20'); logs.value = r.data } catch { /* */ } finally { loading.value = false } }
async function trigger() {
  triggering.value = true
  try { await client.post('/admin/scan/trigger') } catch { /* */ }
}
async function cancelScan() {
  cancelling.value = true
  try { await client.post('/admin/scan/cancel') } catch { /* */ }
}

const busy = computed(() => status.value.isRunning || cancelling.value || triggering.value)
async function clearCache() {
  try {
    await client.delete('/admin/thumbnails/cache')
    ElMessage.success('缩略图缓存已清除')
  } catch (e: any) { ElMessage.error(e.response?.data?.error || '操作失败') }
}
async function regenerate() {
  try {
    await client.post('/admin/thumbnails/regenerate')
    ElMessage.success('缩略图重建已启动')
  } catch (e: any) { ElMessage.error(e.response?.data?.error || '操作失败') }
}

</script>

<template>
  <div style="padding:16px">
    <el-card style="margin-bottom:16px">
      <template #header>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <span>扫描状态</span>
          <div style="display:flex;gap:8px">
            <el-button type="primary" @click="trigger" :disabled="busy" :loading="status.isRunning">
              触发全量扫描
            </el-button>
            <el-button v-if="status.isRunning" type="danger" @click="cancelScan" :disabled="cancelling" :loading="cancelling">
              中断扫描
            </el-button>
            <el-button @click="clearCache" :disabled="busy">清除缩略图缓存</el-button>
            <el-button type="warning" @click="regenerate" :disabled="busy">重建缩略图</el-button>
          </div>
        </div>
      </template>
      <div v-if="status.isRunning" style="display:flex;gap:16px;margin-bottom:12px;flex-wrap:wrap;font-size:13px;color:var(--el-text-color-secondary)">
        <span>模式: <b>{{ status.mode === 'full' ? '全量' : status.mode === 'incremental' ? '增量' : status.mode }}</b></span>
        <span>已处理: <b>{{ status.processedFiles >= 0 ? status.processedFiles.toLocaleString() : '枚举文件中...' }}</b><template v-if="status.totalFiles > 0"> / {{ status.totalFiles.toLocaleString() }}</template></span>
      </div>
      <el-progress v-if="status.isRunning" :percentage="status.estimatedPercent" :stroke-width="16" striped striped-flow />
      <div v-else style="color:var(--el-text-color-secondary);font-size:13px">空闲</div>
    </el-card>

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
  </div>
</template>
