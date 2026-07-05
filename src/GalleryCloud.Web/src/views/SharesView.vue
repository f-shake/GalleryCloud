<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import client from '../api/client'

interface ShareItem {
  id: string; name: string; token: string
  expiresAt: string | null; createdAt: string; photoCount: number
}

const shares = ref<ShareItem[]>([])
const loading = ref(false)

onMounted(loadShares)

async function loadShares() {
  loading.value = true
  try {
    const r = await client.get('/shares')
    shares.value = r.data
  } catch { /* */ }
  finally { loading.value = false }
}

async function createShare() {
  try {
    const { value } = await ElMessageBox.prompt('分享名称', '新建分享', {
      confirmButtonText: '创建', cancelButtonText: '取消',
      inputValue: '我的分享',
      inputPlaceholder: '分享名称',
    })
    if (!value) return
    const res = await client.post('/shares', { name: value, expireDays: 30 })
    shares.value.unshift(res.data.share)
    ElMessage.success('分享已创建')
  } catch { /* */ }
}

function getShareLink(token: string): string {
  return `${window.location.origin}${import.meta.env.BASE_URL}share/${token}`
}

async function copyLink(token: string) {
  try {
    await navigator.clipboard.writeText(getShareLink(token))
    ElMessage.success('链接已复制')
  } catch {
    // Fallback
    const ta = document.createElement('textarea')
    ta.value = getShareLink(token)
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success('链接已复制')
  }
}

async function deleteShare(id: string) {
  try {
    await ElMessageBox.confirm('确定删除此分享？已有链接将失效。', '删除分享', {
      confirmButtonText: '确定', cancelButtonText: '取消', type: 'warning',
    })
    await client.delete(`/shares/${id}`)
    shares.value = shares.value.filter(s => s.id !== id)
    ElMessage.success('已删除')
  } catch { /* */ }
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '永久'
  const d = new Date(dateStr)
  if (isNaN(d.getTime())) return dateStr
  return d.toLocaleDateString('zh-CN') + ' ' + d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

function isExpired(expiresAt: string | null): boolean {
  if (!expiresAt) return false
  return new Date(expiresAt) < new Date()
}
</script>

<template>
  <div class="sv-wrap">
    <div class="sv-header">
      <span style="font-size:16px;font-weight:600">照片分享</span>
      <el-button type="primary" size="small" @click="createShare">
        <el-icon style="margin-right:3px"><Plus /></el-icon>新建分享
      </el-button>
    </div>

    <el-empty v-if="!loading && shares.length === 0" description="暂无分享" />

    <div v-else-if="shares.length" class="sv-list">
      <el-card v-for="s in shares" :key="s.id" class="sv-card" :class="{ 'sv-card--expired': isExpired(s.expiresAt) }">
        <div class="sv-card-body">
          <div class="sv-card-info">
            <div class="sv-card-name">{{ s.name }}</div>
            <div class="sv-card-meta">
              <span>{{ s.photoCount }} 张照片</span>
              <span>到期：{{ formatDate(s.expiresAt) }}</span>
              <span>创建于 {{ formatDate(s.createdAt) }}</span>
            </div>
          </div>
          <div class="sv-card-actions">
            <el-button size="small" @click="copyLink(s.token)">
              <el-icon style="margin-right:2px"><Link /></el-icon>复制链接
            </el-button>
            <el-button size="small" type="danger" plain @click="deleteShare(s.id)">
              <el-icon style="margin-right:2px"><Delete /></el-icon>删除
            </el-button>
          </div>
        </div>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.sv-wrap { padding: 16px; height: 100%; overflow-y: auto; }
.sv-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
.sv-list { display: flex; flex-direction: column; gap: 8px; max-width: 600px; }
.sv-card--expired { opacity: 0.5; }
.sv-card-body { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.sv-card-info { flex: 1; min-width: 0; }
.sv-card-name { font-size: 14px; font-weight: 600; margin-bottom: 4px; }
.sv-card-meta { display: flex; gap: 12px; font-size: 12px; color: var(--el-text-color-secondary); flex-wrap: wrap; }
.sv-card-actions { display: flex; gap: 6px; flex-shrink: 0; }
</style>
