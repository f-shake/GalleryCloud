<script setup lang="ts">
import { ref } from 'vue'
const active = ref('dashboard')
</script>

<template>
  <div style="padding:0;height:100%;overflow-y:auto">
    <el-tabs v-model="active" style="padding:0 16px" @tab-click="(t: any) => $router.push(`/admin${t.paneName === 'dashboard' ? '' : '/' + t.paneName}`)">
      <el-tab-pane label="仪表盘" name="dashboard" />
      <el-tab-pane label="用户" name="users" />
      <el-tab-pane label="设置" name="settings" />
      <el-tab-pane label="扫描" name="scan" />
    </el-tabs>
    <router-view />
  </div>
</template>
