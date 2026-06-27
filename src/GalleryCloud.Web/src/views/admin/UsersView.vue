<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import client from '../../api/client'
import type { User } from '../../types'

const users = ref<(User & { isActive: boolean })[]>([])
const loading = ref(true)
const showDialog = ref(false)
const isEdit = ref(false)
const form = ref({ id: '', username: '', password: '', displayName: '', rootPath: '', isAdmin: false, isActive: true })

onMounted(() => loadUsers())

async function loadUsers() {
  loading.value = true
  try { const r = await client.get('/admin/users'); users.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
}

function openCreate() {
  isEdit.value = false
  form.value = { id: '', username: '', password: '', displayName: '', rootPath: '', isAdmin: false, isActive: true }
  showDialog.value = true
}

function openEdit(u: any) {
  isEdit.value = true
  form.value = { ...u, password: '' }
  showDialog.value = true
}

async function save() {
  try {
    if (isEdit.value) {
      const body: any = { displayName: form.value.displayName, rootPath: form.value.rootPath, isAdmin: form.value.isAdmin, isActive: form.value.isActive }
      if (form.value.password) body.password = form.value.password
      await client.put(`/admin/users/${form.value.id}`, body)
    } else {
      await client.post('/admin/users', form.value)
    }
    showDialog.value = false
    await loadUsers()
    ElMessage.success('保存成功')
  } catch (e: any) { ElMessage.error(e.response?.data?.error || '操作失败') }
}

async function toggleUser(u: any) {
  try {
    await client.put(`/admin/users/${u.id}`, { isActive: !u.isActive })
    await loadUsers()
  } catch { /* */ }
}
</script>

<template>
  <div style="padding:16px">
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
      <h3 style="margin:0">用户管理</h3>
      <el-button type="primary" :icon="'Plus'" @click="openCreate">新建用户</el-button>
    </div>

    <el-table :data="users" v-loading="loading" stripe>
      <el-table-column prop="username" label="用户名" />
      <el-table-column prop="displayName" label="显示名" />
      <el-table-column prop="rootPath" label="根目录" min-width="200" />
      <el-table-column label="管理员" width="80">
        <template #default="{ row }"><el-tag v-if="row.isAdmin" type="warning" size="small">是</el-tag></template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'danger'" size="small">{{ row.isActive ? '启用' : '禁用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button size="small" @click="openEdit(row)">编辑</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleUser(row)">
            {{ row.isActive ? '禁用' : '启用' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="showDialog" :title="isEdit ? '编辑用户' : '新建用户'" width="480px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="用户名" v-if="!isEdit"><el-input v-model="form.username" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" :placeholder="isEdit ? '留空则不修改' : ''" /></el-form-item>
        <el-form-item label="显示名"><el-input v-model="form.displayName" /></el-form-item>
        <el-form-item label="根目录"><el-input v-model="form.rootPath" /></el-form-item>
        <el-form-item label="管理员"><el-switch v-model="form.isAdmin" /></el-form-item>
        <el-form-item label="状态" v-if="isEdit"><el-switch v-model="form.isActive" active-text="启用" inactive-text="禁用" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showDialog = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
