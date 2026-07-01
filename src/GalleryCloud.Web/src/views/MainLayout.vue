<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { useDark } from '../composables/useDark'
import client from '../api/client'
import { ElMessage } from 'element-plus'
import PhotoDetailView from './PhotoDetailView.vue'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const { isDark, toggleDark } = useDark()

const isMobile = ref(window.innerWidth < 768)
const drawerVisible = ref(false)
const isScanning = ref(false)

// Password change
const pwDialogVisible = ref(false)
const pwForm = ref({ oldPassword: '', newPassword: '', confirmPassword: '' })
const pwLoading = ref(false)

async function handleChangePassword() {
  if (pwForm.value.newPassword !== pwForm.value.confirmPassword) {
    ElMessage.error('两次密码不一致')
    return
  }
  pwLoading.value = true
  try {
    await client.put('/user/password', {
      oldPassword: pwForm.value.oldPassword,
      newPassword: pwForm.value.newPassword,
    })
    ElMessage.success('密码已修改')
    pwDialogVisible.value = false
    pwForm.value = { oldPassword: '', newPassword: '', confirmPassword: '' }
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '修改失败')
  } finally {
    pwLoading.value = false
  }
}

let scanTimer: any = null
async function checkScan() {
  try { const r = await client.get('/user/scan/status'); isScanning.value = r.data.isRunning }
  catch { /* */ }
}

function onResize() { isMobile.value = window.innerWidth < 768 }
onMounted(() => {
  window.addEventListener('resize', onResize)
  if (!auth.isAdmin) { checkScan(); scanTimer = setInterval(checkScan, 5000) }
})
onUnmounted(() => {
  window.removeEventListener('resize', onResize)
  if (scanTimer) clearInterval(scanTimer)
})

const navItems = computed(() => {
  if (auth.isAdmin) {
    return [{ path: '/admin', title: '管理', icon: 'Setting' }]
  }
  return [
    { path: '/timeline', title: '时间线', icon: 'Picture' },
    { path: '/folders', title: '文件夹', icon: 'FolderOpened' },
    { path: '/map', title: '地图', icon: 'MapLocation' },
    { path: '/search', title: '搜索', icon: 'Search' },
    { path: '/favorites', title: '收藏', icon: 'Star' },
    { path: '/manage', title: '管理', icon: 'Setting' },
  ]
})

function navigate(path: string) {
  router.push(path)
  drawerVisible.value = false
}

function handleLogout() {
  auth.logout()
  router.push('/login')
}

const currentTitle = computed(() => navItems.value.find(n => route.path.startsWith(n.path))?.title || '')
</script>

<template>
  <el-container class="app-shell">
    <!-- ==================== Desktop sidebar ==================== -->
    <el-aside v-if="!isMobile" width="200px" class="app-sidebar">
      <div class="sidebar-header">
        <el-icon :size="24" color="var(--el-color-primary)"><PictureFilled /></el-icon>
        <span style="font-weight:700;margin-left:8px;font-size:16px">GalleryCloud</span>
      </div>
      <el-menu :default-active="route.path" @select="(k: string) => navigate(k)" style="border-right:none">
        <el-menu-item v-for="n in navItems" :key="n.path" :index="n.path">
          <el-icon><component :is="n.icon" /></el-icon>
          <span>{{ n.title }}</span>
        </el-menu-item>
      </el-menu>
    </el-aside>

    <!-- ==================== Mobile: top bar + drawer ==================== -->
    <el-container v-else>
      <el-header class="topbar">
        <el-button text @click="drawerVisible = true"><el-icon :size="22"><Menu /></el-icon></el-button>
        <span style="font-weight:700">GalleryCloud</span>
        <div style="flex:1" />
        <el-button text circle @click="toggleDark" class="topbar-icon">
          <el-icon :size="18"><component :is="isDark ? 'Sunny' : 'Moon'" /></el-icon>
        </el-button>
        <!-- User popover -->
        <el-dropdown trigger="click" placement="bottom-end">
          <el-button text class="user-btn">{{ auth.user?.username }}</el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item @click="pwDialogVisible = true">
                <el-icon><EditPen /></el-icon>修改密码
              </el-dropdown-item>
              <el-dropdown-item divided @click="handleLogout()">
                <el-icon><SwitchButton /></el-icon>退出登录
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>

      <el-drawer v-model="drawerVisible" direction="ltr" size="220px" :with-header="false">
        <div style="padding:12px 0;display:flex;flex-direction:column;height:100%">
          <div style="padding:8px 20px;margin-bottom:8px;display:flex;align-items:center">
            <el-icon :size="24" color="var(--el-color-primary)"><PictureFilled /></el-icon>
            <span style="font-weight:700;margin-left:8px">GalleryCloud</span>
          </div>
          <el-menu :default-active="route.path" @select="(k: string) => navigate(k)" style="border-right:none">
            <el-menu-item v-for="n in navItems" :key="n.path" :index="n.path">
              <el-icon><component :is="n.icon" /></el-icon>
              <span>{{ n.title }}</span>
            </el-menu-item>
          </el-menu>
        </div>
      </el-drawer>

      <el-main class="app-main">
        <el-alert v-if="isScanning" title="扫描进行中，照片列表可能不完整" type="info" show-icon :closable="false" class="scan-alert" />
        <router-view />
      </el-main>
    </el-container>

    <!-- ==================== Desktop main ==================== -->
    <el-container v-if="!isMobile">
      <el-header class="topbar">
        <span class="topbar-subtitle">{{ currentTitle }}</span>
        <div style="flex:1" />
        <el-button text circle @click="toggleDark" class="topbar-icon">
          <el-icon :size="18"><component :is="isDark ? 'Sunny' : 'Moon'" /></el-icon>
        </el-button>
        <!-- User dropdown -->
        <el-dropdown trigger="click" placement="bottom-end">
          <el-button text class="user-btn">{{ auth.user?.username }}</el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item @click="pwDialogVisible = true">
                <el-icon><EditPen /></el-icon>修改密码
              </el-dropdown-item>
              <el-dropdown-item divided @click="handleLogout()">
                <el-icon><SwitchButton /></el-icon>退出登录
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>
      <el-main class="app-main">
        <el-alert v-if="isScanning" title="扫描进行中，照片列表可能不完整" type="info" show-icon :closable="false" class="scan-alert" />
        <router-view />
      </el-main>
    </el-container>

    <PhotoDetailView />

    <!-- Password change dialog -->
    <el-dialog v-model="pwDialogVisible" title="修改密码" width="380px">
      <el-form :model="pwForm" label-width="80px">
        <el-form-item label="当前密码"><el-input v-model="pwForm.oldPassword" type="password" /></el-form-item>
        <el-form-item label="新密码"><el-input v-model="pwForm.newPassword" type="password" /></el-form-item>
        <el-form-item label="确认密码"><el-input v-model="pwForm.confirmPassword" type="password" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pwDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleChangePassword" :loading="pwLoading">确认</el-button>
      </template>
    </el-dialog>
  </el-container>
</template>

<style>
html, body, #app { margin:0; height:100%; }
.app-shell { height:100%; touch-action: pan-y; }
.app-sidebar {
  background: var(--el-bg-color);
  border-right: 1px solid var(--el-border-color-light);
  display: flex; flex-direction: column;
}
.sidebar-header {
  padding: 16px 20px; display: flex; align-items: center;
}
/* Unified topbar for both desktop and mobile */
.topbar {
  display: flex; align-items: center; gap: 12px;
  height: 52px; padding: 0 20px;
  background: var(--el-bg-color);
  border-bottom: 1px solid var(--el-border-color-light);
}
@media (max-width: 767px) {
  .topbar { padding: 0 12px; gap: 8px; }
}
.topbar-subtitle {
  font-size: 13px; color: var(--el-text-color-secondary);
}
.user-btn {
  font-size: 13px; color: var(--el-text-color-secondary);
}
.topbar-icon {
  color: var(--el-text-color-secondary);
}

.app-main {
  background: var(--el-bg-color-page);
  overflow-y: auto;
  scrollbar-width: none;
  position: relative; /* anchor for absolute children */
}
.app-main::-webkit-scrollbar { display: none; }
.scan-alert { margin: 8px 16px 0; border-radius: 8px; }
</style>
