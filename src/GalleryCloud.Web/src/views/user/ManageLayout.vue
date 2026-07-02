<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
const route = useRoute()
const active = ref(route.path === '/manage/scan' ? 'scan' : 'dashboard')
</script>

<template>
  <div style="padding:0;height:100%;display:flex;flex-direction:column;overflow:hidden">
    <el-tabs v-model="active" style="padding:0 16px;flex-shrink:0" @tab-click="(t: any) => $router.push(`/manage${t.paneName === 'dashboard' ? '' : '/' + t.paneName}`)">
      <el-tab-pane label="仪表盘" name="dashboard" />
      <el-tab-pane label="扫描" name="scan" />
    </el-tabs>
    <router-view style="flex:1;overflow:hidden" />
  </div>
</template>
