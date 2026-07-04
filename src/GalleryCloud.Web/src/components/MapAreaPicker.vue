<script setup lang="ts">
import { ref, watch, onUnmounted, nextTick } from 'vue'
import client from '../api/client'
import { useMap } from '../composables/useMap'
import Graphic from '@arcgis/core/Graphic'
import Polygon from '@arcgis/core/geometry/Polygon'
import GraphicsLayer from '@arcgis/core/layers/GraphicsLayer'
import SketchViewModel from '@arcgis/core/widgets/Sketch/SketchViewModel'
import SimpleFillSymbol from '@arcgis/core/symbols/SimpleFillSymbol'

const props = defineProps<{
  modelValue: boolean
  south?: number
  north?: number
  west?: number
  east?: number
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  confirm: [bounds: { south: number; north: number; west: number; east: number } | null]
}>()

const mapContainer = ref<HTMLDivElement | null>(null)
const { loading, initMap, switchBasemap, updateTileUrls, destroy: destroyMap } = useMap(mapContainer)
const basemap = ref<'normal' | 'satellite'>('normal')
const hasSelection = ref(false)
const drawing = ref(false)

let viewInst: any = null
let sketchLayer: GraphicsLayer | null = null
let sketchVM: SketchViewModel | null = null
let initPromise: Promise<void> | null = null

function buildRectPolygon(south: number, north: number, west: number, east: number) {
  return new Polygon({
    spatialReference: { wkid: 4326 },
    rings: [[[west, south], [east, south], [east, north], [west, north], [west, south]]]
  })
}

function toGeo(ext: any): { ymin: number; ymax: number; xmin: number; xmax: number } {
  let { xmin, xmax, ymin, ymax } = ext
  if (ext.spatialReference && !ext.spatialReference.isGeographic) {
    const half = 20037508.34
    xmin = (xmin / half) * 180
    xmax = (xmax / half) * 180
    ymin = Math.atan(Math.exp(ymin / half * Math.PI)) * 360 / Math.PI - 90
    ymax = Math.atan(Math.exp(ymax / half * Math.PI)) * 360 / Math.PI - 90
  }
  return { ymin, ymax, xmin, xmax }
}

function readExtent(): { south: number; north: number; west: number; east: number; areaKm2: number } | null {
  if (!sketchLayer || sketchLayer.graphics.length === 0) return null
  const ext = sketchLayer.graphics.getItemAt(0).geometry.extent
  if (!ext) return null
  const geo = toGeo(ext)
  // Approximate area from lat/lng bounds
  const latCenter = (geo.ymin + geo.ymax) / 2 * Math.PI / 180
  const degLen = 111320 // meters per degree latitude
  const w = (geo.xmax - geo.xmin) * degLen * Math.cos(latCenter)
  const h = (geo.ymax - geo.ymin) * degLen
  const areaKm2 = Math.round((w * h) / 1e6 * 100) / 100
  return { south: geo.ymin, north: geo.ymax, west: geo.xmin, east: geo.xmax, areaKm2 }
}

async function ensureMapInitialized() {
  if (viewInst) return
  if (initPromise) { await initPromise; return }

  initPromise = (async () => {
    try {
      const cfg = (await client.get('/map/basemap-config')).data
      updateTileUrls(cfg.tileUrlNormal, cfg.tileUrlSatellite)
    } catch { /* use defaults */ }

    const savedBasemap = localStorage.getItem('mapBasemap')
    if (savedBasemap === 'normal' || savedBasemap === 'satellite') {
      basemap.value = savedBasemap
    }

    const inst = await initMap([105, 35], 4, basemap.value)
    if (!inst) return
    viewInst = inst.view

    sketchLayer = new GraphicsLayer()
    inst.map.add(sketchLayer)

    sketchVM = new SketchViewModel({
      layer: sketchLayer,
      view: viewInst,
      polygonSymbol: {
        type: 'simple-fill',
        color: [64, 158, 255, 0.15],
        outline: { color: [64, 158, 255], width: 2 },
      } as any,
    })

    sketchVM.on('create', (event: any) => {
      if (event.state === 'start') {
        sketchLayer!.removeAll()
        hasSelection.value = false
        drawing.value = true
      } else if (event.state === 'active') {
        drawing.value = true
      } else if (event.state === 'complete') {
        drawing.value = false
        hasSelection.value = true
      } else if (event.state === 'cancel') {
        drawing.value = false
      }
    })

    sketchVM.on('update', (event: any) => {
      if (event.state === 'complete') {
        hasSelection.value = sketchLayer!.graphics.length > 0
      }
    })

    if (props.south != null && props.north != null && props.west != null && props.east != null) {
      const polygon = buildRectPolygon(props.south, props.north, props.west, props.east)
      const symbol = new SimpleFillSymbol({ color: [64, 158, 255, 0.15], outline: { color: [64, 158, 255], width: 2 } })
      sketchLayer.add(new Graphic({ geometry: polygon, symbol }))
      hasSelection.value = true
      viewInst.goTo({ target: polygon, padding: 40 }, { duration: 300 })
    }
  })()

  await initPromise
  initPromise = null
}

function startDraw() {
  if (!sketchVM) return
  hasSelection.value = false
  drawing.value = true
  sketchVM.create('rectangle')
}

function toggleBasemap() {
  basemap.value = basemap.value === 'normal' ? 'satellite' : 'normal'
  localStorage.setItem('mapBasemap', basemap.value)
  switchBasemap(basemap.value)
}

function cancelDraw() {
  sketchVM?.cancel()
  drawing.value = false
}

function clearSelection() {
  sketchLayer?.removeAll()
  sketchVM?.cancel()
  drawing.value = false
  hasSelection.value = false
}

function confirmSelection() {
  const ext = readExtent()
  emit('confirm', ext)
  emit('update:modelValue', false)
}

function closeDialog() {
  sketchLayer?.removeAll()
  hasSelection.value = false
  emit('update:modelValue', false)
}

function teardown() {
  if (sketchVM) { sketchVM.destroy(); sketchVM = null }
  if (sketchLayer) { viewInst?.map.remove(sketchLayer); sketchLayer = null }
  viewInst = null; initPromise = null
  destroyMap()
}

watch(() => props.modelValue, async (val) => {
  if (val) {
    await nextTick()
    await ensureMapInitialized()
  } else {
    teardown()
  }
})

onUnmounted(() => { teardown() })
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    @update:model-value="closeDialog"
    title="选择地图区域"
    width="90%"
    top="2vh"
    :close-on-click-modal="false"
    class="map-area-dialog"
  >
    <div class="map-area-body">
      <div ref="mapContainer" class="map-area-map" :class="{ 'map-area-map--loading': loading }">
        <div v-if="loading" class="map-area-loading"><el-icon class="is-loading" :size="28"><Loading /></el-icon></div>
        <div v-show="!loading" class="map-area-map-btns">
          <div class="map-area-zoom-btns">
            <el-button circle size="default" @click="viewInst?.zoomIn()" title="放大">
              <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            </el-button>
            <el-button circle size="default" @click="viewInst?.zoomOut()" title="缩小">
              <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="5" y1="12" x2="19" y2="12"/></svg>
            </el-button>
          </div>
          <el-button circle size="default" class="map-area-basemap-btn" @click="toggleBasemap" :title="basemap === 'normal' ? '卫星图' : '普通图'">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/>
              <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
            </svg>
          </el-button>
        </div>
        <div v-if="!loading" class="map-area-toolbar">
          <el-button v-if="drawing" size="small" @click="cancelDraw">取消框选</el-button>
          <el-button size="small" type="primary" :disabled="drawing" @click="startDraw">
            {{ hasSelection ? '重新框选区域' : '框选矩形区域' }}
          </el-button>
        </div>
      </div>
    </div>

    <div class="map-area-footer">
      <div v-if="hasSelection && readExtent()" class="map-area-coords">
        <span class="map-area-coord-label">已选</span>
        <span class="map-area-coord-val">{{ readExtent()!.areaKm2 }} km²</span>
      </div>
      <div v-else class="map-area-coords map-area-coords--hint">
        <span>点击底部按钮，在地图上拖动绘制矩形</span>
      </div>
      <div class="map-area-actions">
        <el-button size="small" @click="clearSelection" :disabled="!hasSelection">清除</el-button>
        <el-button size="small" type="primary" @click="confirmSelection" :disabled="!hasSelection">确认选择</el-button>
      </div>
    </div>
  </el-dialog>
</template>

<style scoped>
.map-area-dialog :deep(.el-dialog__body) { padding: 0; display: flex; flex-direction: column; height: 70vh; }
.map-area-body { flex: 1; position: relative; overflow: hidden; }
.map-area-map { width: 100%; height: 100%; min-height: 300px; position: relative; }
.map-area-map--loading { display: flex; align-items: center; justify-content: center; background: var(--el-fill-color-light); }
.map-area-loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 10; background: var(--el-bg-color-page); opacity: 0.8; }
.map-area-toolbar {
  position: absolute; bottom: 16px; left: 50%; transform: translateX(-50%);
  z-index: 10; display: flex; gap: 8px;
}
.map-area-footer {
  display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px;
  padding: 10px 16px; border-top: 1px solid var(--el-border-color-light);
  flex-shrink: 0;
}
.map-area-coords { display: flex; align-items: center; gap: 10px; font-size: 12px; flex-wrap: wrap; }
.map-area-coords--hint { color: var(--el-text-color-secondary); }
.map-area-coord-label { font-weight: 600; color: var(--el-text-color-primary); white-space: nowrap; font-size: 13px; }
.map-area-coord-val { color: var(--el-text-color-regular); }
.map-area-actions { display: flex; gap: 6px; flex-shrink: 0; }
.map-area-map-btns {
  position: absolute; z-index: 10; pointer-events: none;
  inset: 0;
}
.map-area-zoom-btns {
  position: absolute; bottom: 8px; left: 12px;
  display: flex; flex-direction: column;
  align-items: center; gap: 4px;
  pointer-events: auto;
}
.map-area-zoom-btns .el-button.is-circle + .el-button.is-circle { margin-left: 0; }
.map-area-basemap-btn {
  position: absolute; bottom: 8px; right: 12px;
  pointer-events: auto;
}
</style>
