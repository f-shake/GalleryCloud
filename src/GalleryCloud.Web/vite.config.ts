import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'
import UnoCSS from 'unocss/vite'
import path from 'node:path'
import fs from 'node:fs'

const ASSETS_SRC = path.resolve('node_modules/@arcgis/core/assets')
const SERVE_PATH = '/esri-assets'
const MIME: Record<string, string> = {
  '.js': 'application/javascript', '.json': 'application/json',
  '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2', '.woff': 'font/woff', '.wasm': 'application/wasm',
}

function arcgisAssets(): Plugin {
  return {
    name: 'arcgis-assets',
    configureServer(server) {
      server.middlewares.use(SERVE_PATH, (req, res) => {
        const filePath = path.resolve(ASSETS_SRC, req.url!.slice(1).split('?')[0])
        if (!filePath.startsWith(ASSETS_SRC)) { res.statusCode = 403; res.end(); return }
        if (fs.existsSync(filePath) && fs.statSync(filePath).isFile()) {
          const ext = path.extname(filePath)
          res.setHeader('Content-Type', MIME[ext] || 'application/octet-stream')
          res.setHeader('Cache-Control', 'public, max-age=3600')
          res.end(fs.readFileSync(filePath))
        } else {
          res.statusCode = 404; res.end()
        }
      })
    },
    writeBundle() {
      const dest = path.resolve('dist/esri-assets')
      if (!fs.existsSync(dest)) fs.mkdirSync(dest, { recursive: true })
      fs.cpSync(ASSETS_SRC, dest, { recursive: true })
    },
  }
}

export default defineConfig(({ command }) => ({
  base: command === 'build' ? '/gallery/' : '/',
  plugins: [
    vue({ template: { compilerOptions: { isCustomElement: tag => tag.startsWith('arcgis-') } } }),
    UnoCSS(),
    arcgisAssets(),
  ],
  server: {
    port: 5175,
    allowedHosts: ['fshake.com'],
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
}))
