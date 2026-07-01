<script setup lang="ts">
import { useRouter } from 'vue-router'
const router = useRouter()
</script>

<template>
  <div style="display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;gap:16px;color:var(--el-text-color-secondary)">
    <el-icon :size="64" color="var(--el-text-color-placeholder)"><WarningFilled /></el-icon>
    <h1 style="margin:0;font-size:48px;color:var(--el-text-color-primary)">404</h1>
    <p style="margin:0;font-size:16px">页面不存在</p>
    <el-button type="primary" @click="router.push('/')">返回首页</el-button>
  </div>
</template>
