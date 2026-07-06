<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import client from '../api/client'

interface ShareItem {
  id: string; name: string; token: string
  expiresAt: string | null; photoCount: number
}

const props = defineProps<{
  modelValue: boolean
  photoIds: string[]
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  'done': []
}>()

const mode = ref<'new' | 'existing'>('new')
const name = ref('我的分享')
const expireDays = ref(30)
const allowDownload = ref(true)
const allowMetadata = ref(true)
const existingShares = ref<ShareItem[]>([])
const selectedShareId = ref('')
const creating = ref(false)
const loadingShares = ref(false)

// 分享结果状态
const step = ref<'form' | 'result'>('form')
const shareLink = ref('')
const shareToken = ref('')

watch(mode, (val) => {
  if (val === 'existing') loadExistingShares()
})

function onOpen() {
  step.value = 'form'
  shareLink.value = ''
  shareToken.value = ''
  if (mode.value === 'existing') loadExistingShares()
}

async function loadExistingShares() {
  loadingShares.value = true
  try {
    const r = await client.get('/shares')
    existingShares.value = r.data || []
  } catch { /* */ }
  finally { loadingShares.value = false }
}

async function confirm() {
  creating.value = true
  try {
    let shareId: string
    let shareTok = ''

    if (mode.value === 'new') {
      const res = await client.post('/shares', {
        name: name.value,
        expireDays: expireDays.value === 0 ? 0 : expireDays.value,
        allowDownload: allowDownload.value,
        allowMetadata: allowMetadata.value,
      })
      shareId = res.data.share.id
      shareTok = res.data.share.token
    } else {
      shareId = selectedShareId.value
      if (!shareId) { ElMessage.warning('请选择要加入的分享'); creating.value = false; return }
    }

    await client.post(`/shares/${shareId}/photos`, { photoIds: props.photoIds })

    if (mode.value === 'new') {
      const link = `${window.location.origin}${import.meta.env.BASE_URL}share/${shareTok}`
      shareLink.value = link
      shareToken.value = shareTok
      step.value = 'result'
      // 自动复制
      try {
        await navigator.clipboard.writeText(link)
      } catch {
        /* 静默降级，用户可手动点击复制按钮 */
      }
    } else {
      ElMessage.success(`已加入 ${props.photoIds.length} 张照片`)
      emit('update:modelValue', false)
      emit('done')
    }
  } catch { ElMessage.error('分享失败') }
  finally { creating.value = false }
}

async function copyLink() {
  if (!shareLink.value) return
  try {
    await navigator.clipboard.writeText(shareLink.value)
    ElMessage.success('链接已复制')
  } catch {
    // Fallback for older browsers
    const ta = document.createElement('textarea')
    ta.value = shareLink.value
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success('链接已复制')
  }
}

function finish() {
  emit('update:modelValue', false)
  emit('done')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    @update:model-value="emit('update:modelValue', $event)"
    :title="step === 'result' ? '分享成功' : '分享照片'"
    width="420px"
    @open="onOpen"
  >
    <!-- 步骤一：创建/选择分享 -->
    <template v-if="step === 'form'">
      <div style="margin-bottom:12px;font-size:13px;color:var(--el-text-color-secondary)">
        已选 {{ photoIds.length }} 张照片
      </div>

      <el-radio-group v-model="mode" style="margin-bottom:12px">
        <el-radio value="new">新建分享</el-radio>
        <el-radio value="existing">加入已有分享</el-radio>
      </el-radio-group>

      <template v-if="mode === 'new'">
        <el-form label-width="80px">
          <el-form-item label="名称">
            <el-input v-model="name" placeholder="分享名称" />
          </el-form-item>
          <el-form-item label="有效期">
            <el-select v-model="expireDays" style="width:100%">
              <el-option :value="1" label="1 天" />
              <el-option :value="7" label="7 天" />
              <el-option :value="30" label="30 天（默认）" />
              <el-option :value="0" label="永久" />
            </el-select>
          </el-form-item>
          <el-form-item label="权限">
            <el-checkbox v-model="allowDownload" style="margin-right:12px">允许下载原图</el-checkbox>
            <el-checkbox v-model="allowMetadata">允许查看元数据</el-checkbox>
          </el-form-item>
        </el-form>
      </template>

      <template v-else>
        <el-select v-model="selectedShareId" placeholder="选择分享" style="width:100%" :loading="loadingShares">
          <el-option v-for="s in existingShares" :key="s.id" :value="s.id" :label="s.name + '（' + s.photoCount + '张）'" />
        </el-select>
      </template>
    </template>

    <!-- 步骤二：展示分享链接 -->
    <template v-else>
      <div style="margin-bottom:16px;font-size:14px;color:var(--el-text-color-primary)">
        已成功分享 {{ photoIds.length }} 张照片
      </div>
      <div class="share-result-link">
        <input
          ref="linkInput"
          :value="shareLink"
          class="share-link-input"
          readonly
          @focus="($event.target as HTMLInputElement).select()"
        />
        <el-button type="primary" size="small" @click="copyLink">
          <el-icon style="margin-right:2px"><CopyDocument /></el-icon>复制
        </el-button>
      </div>
      <div style="margin-top:8px;font-size:12px;color:var(--el-text-color-secondary)">
        链接已自动复制到剪贴板
      </div>
    </template>

    <template #footer>
      <template v-if="step === 'form'">
        <el-button @click="emit('update:modelValue', false)">取消</el-button>
        <el-button type="primary" :loading="creating" @click="confirm">确定</el-button>
      </template>
      <template v-else>
        <el-button type="primary" @click="finish">完成</el-button>
      </template>
    </template>
  </el-dialog>
</template>

<style scoped>
.share-result-link {
  display: flex;
  gap: 8px;
  align-items: center;
}
.share-link-input {
  flex: 1;
  padding: 8px 10px;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  font-size: 13px;
  outline: none;
  cursor: text;
}
.share-link-input:focus {
  border-color: var(--el-color-primary);
}
</style>
