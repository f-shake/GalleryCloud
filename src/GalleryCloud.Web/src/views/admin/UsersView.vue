<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import client from '../../api/client'
import FolderBrowser from '../../components/FolderBrowser.vue'
import type { UserRoot } from '../../types'

interface UserRow {
  id: string
  username: string
  displayName: string | null
  isActive: boolean
  createdAt: string
  roots: UserRoot[]
}

const users = ref<UserRow[]>([])
const loading = ref(true)
const showDialog = ref(false)
const isEdit = ref(false)
const form = ref({ id: '', username: '', password: '', displayName: '', rootPaths: [] as string[], isActive: true })
const editRootPaths = ref<string[]>([])
const showRootsDialog = ref(false)
const editingUser = ref<string | null>(null)
const saving = ref(false)

// Folder browser state
const showFolderBrowser = ref(false)
const folderBrowserTarget = ref<{ mode: 'create'; index: number } | { mode: 'edit'; index: number } | null>(null)

function openFolderBrowser(target: typeof folderBrowserTarget.value) {
  folderBrowserTarget.value = target
  showFolderBrowser.value = true
}

function onFolderSelected(path: string) {
  const target = folderBrowserTarget.value
  if (!target) return
  if (target.mode === 'create') {
    form.value.rootPaths[target.index] = path
  } else {
    editRootPaths.value[target.index] = path
  }
}

onMounted(() => loadUsers())

async function loadUsers() {
  loading.value = true
  try { const r = await client.get('/admin/users'); users.value = r.data }
  catch { /* */ }
  finally { loading.value = false }
}

function openCreate() {
  isEdit.value = false
  form.value = { id: '', username: '', password: '', displayName: '', rootPaths: [''], isActive: true }
  showDialog.value = true
}

async function openEdit(u: UserRow) {
  isEdit.value = true
  form.value = { id: u.id, username: u.username, password: '', displayName: u.displayName || '', rootPaths: [], isActive: u.isActive }
  editRootPaths.value = u.roots.map(r => r.rootPath)
  editingUser.value = u.id
  showRootsDialog.value = true
}

function closeEdit() {
  showRootsDialog.value = false
  editingUser.value = null
  editRootPaths.value = []
}

async function save() {
  saving.value = true
  try {
    if (isEdit.value) {
      const rootPaths = editRootPaths.value.filter(p => p.trim())
      if (rootPaths.length === 0) {
        ElMessage.error('至少需要一个根目录')
        return
      }
      const body: any = { displayName: form.value.displayName, isActive: form.value.isActive, rootPaths }
      if (form.value.password) body.password = form.value.password
      await client.put(`/admin/users/${form.value.id}`, body)
      showRootsDialog.value = false
    } else {
      const rootPaths = form.value.rootPaths.filter(p => p.trim())
      if (rootPaths.length === 0) {
        ElMessage.error('至少需要一个根目录')
        return
      }
      await client.post('/admin/users', { ...form.value, rootPaths })
      showDialog.value = false
    }
    await loadUsers()
    ElMessage.success('保存成功')
  } catch (e: any) { ElMessage.error(e.response?.data?.error || '操作失败') }
  finally { saving.value = false }
}

function addRoot() {
  editRootPaths.value.push('')
}

function removeRoot(index: number) {
  editRootPaths.value.splice(index, 1)
}

async function toggleUser(u: UserRow) {
  try {
    if (u.isActive) {
      await client.delete(`/admin/users/${u.id}`)
    } else {
      await client.put(`/admin/users/${u.id}`, { isActive: true })
    }
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
      <el-table-column label="根目录" min-width="250">
        <template #default="{ row }">
          <el-tag v-for="root in row.roots" :key="root.id" style="margin:2px" size="small">{{ root.rootPath }}</el-tag>
          <span v-if="row.roots.length === 0" style="color:var(--el-text-color-placeholder);font-size:12px">无</span>
        </template>
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

    <!-- Create dialog -->
    <el-dialog v-model="showDialog" title="新建用户" width="500px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="用户名"><el-input v-model="form.username" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" /></el-form-item>
        <el-form-item label="显示名"><el-input v-model="form.displayName" /></el-form-item>
        <el-form-item label="根目录">
          <div style="width:100%">
            <div v-for="(_, i) in form.rootPaths" :key="i" style="display:flex;gap:4px;margin-bottom:4px;align-items:center">
              <el-input v-model="form.rootPaths[i]" placeholder="输入目录路径或点击浏览选择" />
              <el-button type="primary" @click="openFolderBrowser({ mode: 'create', index: i })" >浏览</el-button>
              <el-button @click="form.rootPaths.splice(i, 1)" :icon="'Delete'" circle size="small" />
            </div>
            <el-button size="small" @click="form.rootPaths.push('')" :icon="'Plus'" style="margin-top:4px" />
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showDialog = false">取消</el-button>
        <el-button type="primary" @click="save" :loading="saving">保存</el-button>
      </template>
    </el-dialog>

    <!-- Edit roots dialog -->
    <el-dialog v-model="showRootsDialog" title="编辑用户" width="500px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="用户名">{{ form.username }}</el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" placeholder="留空则不修改" /></el-form-item>
        <el-form-item label="显示名"><el-input v-model="form.displayName" /></el-form-item>
        <el-form-item label="根目录">
          <div style="width:100%">
            <div v-for="(p, i) in editRootPaths" :key="i" style="display:flex;gap:4px;margin-bottom:4px;align-items:center">
              <el-input v-model="editRootPaths[i]" placeholder="输入目录路径或点击浏览选择" />
              <el-button type="primary" @click="openFolderBrowser({ mode: 'edit', index: i })" >浏览</el-button>
              <el-button @click="removeRoot(i)" :icon="'Delete'" circle size="small" />
            </div>
            <el-button size="small" @click="addRoot" :icon="'Plus'" style="margin-top:4px" />
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeEdit">取消</el-button>
        <el-button type="primary" @click="save" :loading="saving">保存</el-button>
      </template>
    </el-dialog>

    <FolderBrowser
      v-model="showFolderBrowser"
      @selected="onFolderSelected"
    />
  </div>
</template>
