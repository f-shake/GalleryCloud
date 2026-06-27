<script setup lang="ts">
import { ref } from 'vue'
import { User, Lock } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const router = useRouter()
const auth = useAuthStore()

const form = ref({ username: 'admin', password: 'admin' })
const loading = ref(false)
const error = ref('')

async function handleLogin() {
  error.value = ''
  loading.value = true
  const ok = await auth.login(form.value.username, form.value.password)
  loading.value = false
  if (ok) {
    router.push('/timeline')
  } else {
    error.value = '用户名或密码错误'
  }
}
</script>

<template>
  <div class="login-page">
    <el-container class="login-container">
      <el-main class="login-main">
        <div class="login-card">
          <div style="text-align:center;margin-bottom:32px">
            <el-icon :size="48" color="var(--el-color-primary)"><PictureFilled /></el-icon>
            <h2 style="margin:12px 0 4px">GalleryCloud</h2>
            <p style="color:var(--el-text-color-secondary)">私有化智能相册</p>
          </div>

          <el-form @submit.prevent="handleLogin" size="large">
            <el-form-item>
              <el-input v-model="form.username" placeholder="用户名" :prefix-icon="User" />
            </el-form-item>
            <el-form-item>
              <el-input v-model="form.password" type="password" placeholder="密码" show-password
                :prefix-icon="Lock" @keyup.enter="handleLogin" />
            </el-form-item>

            <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" style="margin-bottom:16px" />

            <el-button type="primary" native-type="submit" :loading="loading" style="width:100%">
              登录
            </el-button>
          </el-form>
        </div>
      </el-main>
    </el-container>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  background: var(--el-bg-color-page);
}
.login-container {
  min-height: 100vh;
}
.login-main {
  display: flex;
  align-items: center;
  justify-content: center;
}
.login-card {
  width: 380px;
  max-width: 90vw;
  padding: 40px 32px;
  background: var(--el-bg-color);
  border-radius: 8px;
  box-shadow: var(--el-box-shadow-light);
}
</style>
