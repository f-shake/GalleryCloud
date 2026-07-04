<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
const route = useRoute()
const active = computed(() => route.name === 'admin-settings' ? 'settings' : route.name === 'admin-users' ? 'users' : 'dashboard')
</script>

<template>
  <div style="padding:0;height:100%;overflow-y:auto">
    <el-tabs v-model="active" style="padding:0 16px" @tab-click="(t: any) => $router.push(`/admin${t.paneName === 'dashboard' ? '' : '/' + t.paneName}`)">
      <el-tab-pane label="仪表盘" name="dashboard" />
      <el-tab-pane label="用户" name="users" />
      <el-tab-pane label="设置" name="settings" />
    </el-tabs>
    <router-view />
  </div>
</template>
