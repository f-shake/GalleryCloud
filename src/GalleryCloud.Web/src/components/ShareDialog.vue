<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
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
const existingShares = ref<ShareItem[]>([])
const selectedShareId = ref('')
const creating = ref(false)
const loadingShares = ref(false)

onMounted(() => {
  if (props.modelValue) loadExistingShares()
})

watch(mode, (val) => {
  if (val === 'existing') loadExistingShares()
})

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
    let shareToken = ''

    if (mode.value === 'new') {
      const res = await client.post('/shares', {
        name: name.value,
        expireDays: expireDays.value === 0 ? 0 : expireDays.value,
      })
      shareId = res.data.share.id
      shareToken = res.data.share.token
    } else {
      shareId = selectedShareId.value
      if (!shareId) { ElMessage.warning('请选择要加入的分享'); creating.value = false; return }
    }

    await client.post(`/shares/${shareId}/photos`, { photoIds: props.photoIds })

    // Copy link if new share
    if (mode.value === 'new') {
      const link = `${window.location.origin}${import.meta.env.BASE_URL}share/${shareToken}`
      await navigator.clipboard.writeText(link)
      ElMessage.success(`已分享 ${props.photoIds.length} 张照片，链接已复制`)
    } else {
      ElMessage.success(`已加入 ${props.photoIds.length} 张照片`)
    }

    emit('update:modelValue', false)
    emit('done')
  } catch { ElMessage.error('分享失败') }
  finally { creating.value = false }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    @update:model-value="emit('update:modelValue', $event)"
    title="分享照片"
    width="400px"
    @open="mode === 'new' ? undefined : loadExistingShares()"
  >
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
      </el-form>
    </template>

    <template v-else>
      <el-select v-model="selectedShareId" placeholder="选择分享" style="width:100%" :loading="loadingShares">
        <el-option v-for="s in existingShares" :key="s.id" :value="s.id" :label="s.name + '（' + s.photoCount + '张）'" />
      </el-select>
    </template>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="creating" @click="confirm">确定</el-button>
    </template>
  </el-dialog>
</template>
