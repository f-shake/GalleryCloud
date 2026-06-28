import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import '@arcgis/core/assets/esri/themes/light/main.css'
import esriConfig from '@arcgis/core/config'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import router from './router'
import { vLazyImg } from './composables/useLazyImg'
import App from './App.vue'

esriConfig.assetsPath = import.meta.env.BASE_URL + 'esri-assets'

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
