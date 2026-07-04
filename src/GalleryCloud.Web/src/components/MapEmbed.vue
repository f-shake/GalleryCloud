<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import client from '../api/client'
import { useMap } from '../composables/useMap'
import Graphic from '@arcgis/core/Graphic'
import Point from '@arcgis/core/geometry/Point'
import GraphicsLayer from '@arcgis/core/layers/GraphicsLayer'
import SimpleMarkerSymbol from '@arcgis/core/symbols/SimpleMarkerSymbol'

const props = defineProps<{
  latitude: number | null
  longitude: number | null
}>()

const mapContainer = ref<HTMLDivElement | null>(null)
const { loading, initMap, switchBasemap, updateTileUrls, destroy } = useMap(mapContainer)
const basemap = ref<'normal' | 'satellite'>('normal')
let markerLayer: any = null
let marker: any = null
let mapInst: any = null
let viewInst: any = null
let initialized = false
let initPromise: Promise<void> | null = null

function buildMarker(lat: number, lng: number) {
  return new Graphic({
    geometry: new Point({ longitude: lng, latitude: lat }),
    symbol: new SimpleMarkerSymbol({
      color: [220, 50, 50],
      size: 14,
      outline: { color: [255, 255, 255], width: 2 },
    })
  })
}

function updatePosition(lat: number, lng: number) {
  if (!viewInst || !mapInst) return

  if (markerLayer && marker) {
    markerLayer.remove(marker)
  }

  marker = buildMarker(lat, lng)
  if (!markerLayer) {
    markerLayer = new GraphicsLayer()
    mapInst.add(markerLayer)
  }
  markerLayer.add(marker)
  viewInst.goTo({ center: [lng, lat], zoom: 16 }, { duration: 300 })
}

function clearMarker() {
  if (markerLayer && marker) {
    markerLayer.remove(marker)
    marker = null
  }
}

async function ensureMapInitialized(lat: number, lng: number) {
  if (initialized) return
  if (lat == null || lng == null) return

  // Avoid duplicate initialization if called again while in progress
  if (initPromise) {
    await initPromise
    updatePosition(lat, lng)
    return
  }

  initPromise = (async () => {
    // Load tile URLs from API
    try {
      const cfg = (await client.get('/map/basemap-config')).data
      updateTileUrls(cfg.tileUrlNormal, cfg.tileUrlSatellite)
    } catch { /* use defaults */ }

    // Restore saved basemap preference (shared with MapView)
    const savedBasemap = localStorage.getItem('mapBasemap')
    if (savedBasemap === 'normal' || savedBasemap === 'satellite') {
      basemap.value = savedBasemap
    }

    const inst = await initMap([lng, lat], 16)
    if (!inst) return

    mapInst = inst.map
    viewInst = inst.view
    switchBasemap(basemap.value)

    markerLayer = new GraphicsLayer()
    marker = buildMarker(lat, lng)
    markerLayer.add(marker)
    inst.map.add(markerLayer)
    initialized = true
  })()

  await initPromise
  initPromise = null
}

onMounted(async () => {
  if (props.latitude != null && props.longitude != null) {
    await ensureMapInitialized(props.latitude, props.longitude)
  }
})

watch(() => [props.latitude, props.longitude], async ([lat, lng]) => {
  if (lat != null && lng != null) {
    if (!initialized) {
      // Transitioned from no-GPS → GPS: initialize map first, then marker is set during init
      await ensureMapInitialized(lat, lng)
    } else {
      updatePosition(lat, lng)
    }
  } else {
    // Transitioned from GPS → no-GPS: clear marker, keep map instance
    clearMarker()
  }
})

function toggleBasemap() {
  basemap.value = basemap.value === 'normal' ? 'satellite' : 'normal'
  localStorage.setItem('mapBasemap', basemap.value)
  switchBasemap(basemap.value)
}

onUnmounted(() => {
  initialized = false
  initPromise = null
  destroy()
})
</script>

<template>
  <div class="map-embed-wrapper">
    <div ref="mapContainer" class="map-embed" :class="{ 'map-embed--loading': loading }">
      <div v-if="latitude == null || longitude == null" class="map-embed-empty">
        <el-icon :size="32" color="var(--el-text-color-placeholder)"><MapLocation /></el-icon>
        <span>无空间信息</span>
      </div>
    </div>
    <div v-show="latitude != null && longitude != null && !loading" class="map-embed-zoom">
      <el-button circle size="default" @click="viewInst?.zoomIn()" title="放大">
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
      </el-button>
      <el-button circle size="default" @click="viewInst?.zoomOut()" title="缩小">
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="5" y1="12" x2="19" y2="12"/></svg>
      </el-button>
    </div>
    <el-button v-show="latitude != null && longitude != null && !loading" circle size="default" class="map-embed-basemap-btn" @click="toggleBasemap" :title="basemap === 'normal' ? '卫星图' : '普通图'">
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="12" cy="12" r="10"/>
        <line x1="2" y1="12" x2="22" y2="12"/>
        <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
      </svg>
    </el-button>
  </div>
</template>

<style scoped>
.map-embed-wrapper {
  position: relative;
  width: 100%;
  height: 100%;
}
.map-embed {
  width: 100%;
  height: 100%;
  min-height: 120px;
  border-radius: 8px;
  overflow: hidden;
  position: relative;
  touch-action: none;
}
.map-embed--loading {
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--el-fill-color-light);
}
.map-embed-empty {
  position: absolute;
  inset: 0;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  background: var(--el-fill-color-light);
}
.map-embed-zoom {
  position: absolute;
  bottom: 12px;
  left: 12px;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.map-embed-zoom .el-button.is-circle + .el-button.is-circle { margin-left: 0; }
.map-embed-basemap-btn {
  position: absolute;
  bottom: 12px;
  right: 12px;
  z-index: 1;
}
</style>

<style>
.esri-attribution { display: none !important; }
.esri-zoom { display: none !important; }
</style>
