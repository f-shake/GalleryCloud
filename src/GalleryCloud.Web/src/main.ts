import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import router from './router'
import { vLazyImg } from './composables/useLazyImg'
import App from './App.vue'

// 全局禁止裂图：所有 <img> 加载失败时替换为透明像素
const TRANSPARENT_PIXEL = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'
window.addEventListener('error', (e) => {
  const target = e.target as HTMLElement
  if (target.tagName === 'IMG') {
    (target as HTMLImageElement).src = TRANSPARENT_PIXEL
    e.preventDefault()
  }
}, true)

const app = createApp(App)

// Register all Element Plus icons
for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

app.use(createPinia())
app.use(router)
app.use(ElementPlus, { size: 'default', locale: zhCn })
app.directive('lazy-img', vLazyImg)
app.mount('#app')
