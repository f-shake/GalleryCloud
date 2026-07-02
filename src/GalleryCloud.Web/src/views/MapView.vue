<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { usePhotoViewStore, toDateInt } from '../stores/photoViewStore'
import { useMap, type MapInstance } from '../composables/useMap'
import FeatureLayer from '@arcgis/core/layers/FeatureLayer'
import GraphicsLayer from '@arcgis/core/layers/GraphicsLayer'
import Graphic from '@arcgis/core/Graphic'
import Point from '@arcgis/core/geometry/Point'
import SimpleRenderer from '@arcgis/core/renderers/SimpleRenderer'
import SimpleMarkerSymbol from '@arcgis/core/symbols/SimpleMarkerSymbol'
import PictureMarkerSymbol from '@arcgis/core/symbols/PictureMarkerSymbol'
import { watch as reactiveWatch } from '@arcgis/core/core/reactiveUtils'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'

interface MapPoint { id: string; latitude: number; longitude: number; fileName: string; takenAt: string | null }

const viewStore = usePhotoViewStore()
const { columns, groupLevel, zoomIn, zoomOut } = usePhotoGrid()
const mapContainer = ref<HTMLDivElement | null>(null)
const { loading, initMap, switchBasemap, updateTileUrls, destroy: destroyMap } = useMap(mapContainer)
const pointCount = ref(0)
const basemap = ref<'normal' | 'satellite'>('normal')
const mode = ref<'cluster' | 'point'>('cluster')
let allPoints: MapPoint[] = []
const clusterView = ref<{ lat: number; lng: number; photos: any[]; groups: any[]; loading: boolean } | null>(null)

let mapInst: MapInstance | null = null
let clusterLayer: FeatureLayer | null = null
let dotLayer: FeatureLayer | null = null      // many-dots mode (fast path)
let dotGL: GraphicsLayer | null = null          // density-based dot layer
let thumbGL: GraphicsLayer | null = null        // density-based bubble layer
let extentHandle: any = null
let extentTimer: any = null

function openPhoto(photoId: string, screenPoint?: { x: number; y: number }) {
  const rect = screenPoint
    ? { x: screenPoint.x, y: screenPoint.y, width: 1, height: 1 }
    : { x: 0, y: 0, width: 0, height: 0 }
  viewStore.show(photoId, rect, '', allPoints.map(p => ({ id: p.id, takenAtDate: toDateInt(p.takenAt) })))
}


function makeGraphics(points: MapPoint[]) {
  return points.map((p, i) => new Graphic({
    geometry: new Point({ longitude: p.longitude, latitude: p.latitude }),
    attributes: {
      ObjectID: i + 1,
      photoId: p.id,
      fileName: p.fileName,
      takenAt: p.takenAt ?? '',
      latitude: p.latitude,
      longitude: p.longitude,
    }
  }))
}

function buildClusterLayer(points: MapPoint[]) {
  return new FeatureLayer({
    source: makeGraphics(points),
    objectIdField: 'ObjectID',
    fields: [
      { name: 'ObjectID', type: 'oid' },
      { name: 'photoId', type: 'string' },
      { name: 'fileName', type: 'string' },
      { name: 'takenAt', type: 'string' },
      { name: 'latitude', type: 'double' },
      { name: 'longitude', type: 'double' },
    ],
    renderer: new SimpleRenderer({
      symbol: new SimpleMarkerSymbol({
        color: [64, 158, 255],
        size: 10,
        outline: { color: [255, 255, 255], width: 1.5 },
      })
    }),
    featureReduction: {
      type: 'cluster',
      clusterRadius: 80,
      clusterMinSize: 24,
      labelingInfo: [{
        labelExpressionInfo: { expression: 'Text($feature.cluster_count, \'#,###\')' },
        deconflictionStrategy: 'none',
        labelPlacement: 'center-center',
        symbol: {
          type: 'text',
          color: '#ffffff',
          font: { size: 11, weight: 'bold' },
          haloSize: 1,
          haloColor: '#1d4ed8',
        },
      }],
    } as any,
  })
}

function buildDotLayer(points: MapPoint[]) {
  return new FeatureLayer({
    source: makeGraphics(points),
    objectIdField: 'ObjectID',
    fields: [
      { name: 'ObjectID', type: 'oid' },
      { name: 'photoId', type: 'string' },
      { name: 'fileName', type: 'string' },
      { name: 'takenAt', type: 'string' },
      { name: 'latitude', type: 'double' },
      { name: 'longitude', type: 'double' },
    ],
    renderer: new SimpleRenderer({
      symbol: new SimpleMarkerSymbol({
        color: [64, 158, 255],
        size: 8,
        outline: { color: [255, 255, 255], width: 1.5 },
      })
    }),
    visible: false,
  })
}

function getExtentBounds(): { xmin: number; xmax: number; ymin: number; ymax: number } | null {
  if (!mapInst) return null
  const extent = mapInst.view.extent
  if (!extent) return null
  let { xmin, xmax, ymin, ymax } = extent
  if (extent.spatialReference && !extent.spatialReference.isGeographic) {
    const half = 20037508.34
    xmin = (xmin / half) * 180
    xmax = (xmax / half) * 180
    ymin = Math.atan(Math.exp(ymin / half * Math.PI)) * 360 / Math.PI - 90
    ymax = Math.atan(Math.exp(ymax / half * Math.PI)) * 360 / Math.PI - 90
  }
  return { xmin, xmax, ymin, ymax }
}

function updatePointMode() {
  if (!dotLayer || !dotGL || !thumbGL || !mapInst) return

  const bounds = getExtentBounds()
  if (!bounds) return

  const visible = allPoints.filter(p =>
    p.longitude >= bounds.xmin && p.longitude <= bounds.xmax &&
    p.latitude >= bounds.ymin && p.latitude <= bounds.ymax
  )

  dotGL.removeAll()
  thumbGL.removeAll()

  // Fast path: 50+ visible → all dots, skip density check
  if (visible.length >= 50 || visible.length === 0) {
    dotLayer.visible = true
    thumbGL.visible = false
    dotGL.visible = false
    return
  }

  // Density check: project to screen coords
  const screenPts: { pt: MapPoint; sx: number; sy: number }[] = []
  for (const p of visible) {
    const sp = mapInst!.view.toScreen(new Point({ longitude: p.longitude, latitude: p.latitude }))
    if (sp) screenPts.push({ pt: p, sx: sp.x, sy: sp.y })
  }

  const MIN_DIST = 80 // pixels — bubble would overlap if closer
  const MAX_NEIGHBORS = 2

  const dense: typeof screenPts = []
  const sparse: typeof screenPts = []

  for (const a of screenPts) {
    let n = 0
    for (const b of screenPts) {
      if (a === b) continue
      const dx = a.sx - b.sx, dy = a.sy - b.sy
      if (dx * dx + dy * dy < MIN_DIST * MIN_DIST) n++
      if (n > MAX_NEIGHBORS) break
    }
    ;(n <= MAX_NEIGHBORS ? sparse : dense).push(a)
  }

  dotLayer.visible = false

  // Show dots for dense points
  dotGL.visible = dense.length > 0
  for (const d of dense) {
    dotGL.add(new Graphic({
      geometry: new Point({ longitude: d.pt.longitude, latitude: d.pt.latitude }),
      symbol: new SimpleMarkerSymbol({
        color: [64, 158, 255],
        size: 8,
        outline: { color: [255, 255, 255], width: 1.5 },
      }),
      attributes: { photoId: d.pt.id },
    }))
  }

  // Show raw thumbnails for sparse points
  if (sparse.length > 0) {
    thumbGL.visible = true
    for (const s of sparse) {
      thumbGL.add(new Graphic({
        geometry: new Point({ longitude: s.pt.longitude, latitude: s.pt.latitude }),
        symbol: new PictureMarkerSymbol({
          url: thumbUrl(s.pt.id, 'grid', 120),
          width: 48,
          height: 48,
        }),
        attributes: { photoId: s.pt.id },
      }))
    }
  } else {
    thumbGL.visible = false
  }
}

function switchMode(newMode: 'cluster' | 'point') {
  mode.value = newMode
  localStorage.setItem('mapMode', newMode)
  if (!clusterLayer || !dotLayer || !dotGL || !thumbGL || !mapInst) return

  if (extentHandle) { extentHandle.remove(); extentHandle = null }

  if (newMode === 'cluster') {
    clusterLayer.visible = true
    dotLayer.visible = false
    dotGL.visible = false
    thumbGL.visible = false
  } else {
    clusterLayer.visible = false
    updatePointMode()
    extentHandle = reactiveWatch(() => mapInst!.view.extent, () => {
      clearTimeout(extentTimer)
      extentTimer = setTimeout(() => updatePointMode(), 250)
    })
  }
}

function getRadiusFromZoom(zoom: number): number {
  if (zoom <= 5) return 3.0
  if (zoom <= 8) return 1.0
  if (zoom <= 10) return 0.3
  if (zoom <= 12) return 0.08
  if (zoom <= 14) return 0.02
  return 0.01
}

function groupByDate(photos: any[]) {
  const level = groupLevel.value
  const groups: { label: string; photos: any[] }[] = []
  if (level === 'none' || photos.length === 0) {
    groups.push({ label: '', photos })
    return groups
  }
  let lastKey = ''
  let currentLabel = ''
  let currentBatch: any[] = []
  function flush() {
    if (currentBatch.length > 0) {
      groups.push({ label: currentLabel, photos: currentBatch.splice(0) })
    }
  }
  for (const p of photos) {
    if (!p.takenAt) { currentBatch.push(p); continue }
    const d = new Date(p.takenAt)
    const key = level === 'month'
      ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
      : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    if (key !== lastKey && lastKey !== '') flush()
    lastKey = key
    currentLabel = level === 'month'
      ? `${d.getFullYear()}年${d.getMonth() + 1}月`
      : `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
    currentBatch.push(p)
  }
  flush()
  return groups
}

// Re-group when zoom level changes
watch(groupLevel, () => {
  if (clusterView.value && clusterView.value.photos.length > 0) {
    clusterView.value = {
      ...clusterView.value,
      groups: groupByDate(clusterView.value.photos),
    }
  }
})

function fetchClusterPhotos(lat: number, lng: number, zoom: number) {
  const r2 = getRadiusFromZoom(zoom) ** 2
  clusterView.value = { lat, lng, photos: [], groups: [], loading: true }
  setTimeout(() => {
    const nearby = allPoints
      .filter(p => {
        const dlat = p.latitude - lat
        const dlng = p.longitude - lng
        return dlat * dlat + dlng * dlng < r2
      })
      .sort((a, b) => (b.takenAt || '').localeCompare(a.takenAt || ''))
    clusterView.value = { lat, lng, photos: nearby, groups: groupByDate(nearby), loading: false }
  }, 0)
}

function onPhotoClick(photoId: string, e: MouseEvent) {
  const img = (e.currentTarget as HTMLElement).querySelector('img')
  const r = img ? img.getBoundingClientRect() : (e.currentTarget as HTMLElement).getBoundingClientRect()
  viewStore.show(photoId, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src,
    allPoints.map(p => ({ id: p.id, takenAtDate: toDateInt(p.takenAt) })))
}

function closeClusterView() { clusterView.value = null }

function mapZoomIn() { mapInst?.view.zoomIn() }
function mapZoomOut() { mapInst?.view.zoomOut() }

function toggleBasemap() {
  basemap.value = basemap.value === 'normal' ? 'satellite' : 'normal'
  localStorage.setItem('mapBasemap', basemap.value)
  switchBasemap(basemap.value)
}

let pinchStart = 0, pinchEnd = 0
function onTouchStart(e: TouchEvent) {
  if (e.touches.length === 2) {
    pinchStart = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY)
    pinchEnd = pinchStart
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2 && pinchStart > 0) { e.preventDefault(); pinchEnd = Math.hypot(e.touches[0].clientX - e.touches[1].clientX, e.touches[0].clientY - e.touches[1].clientY) }
}
function onTouchEnd() {
  if (pinchStart > 0 && Math.abs(pinchEnd - pinchStart) > 20) { if (pinchEnd > pinchStart) zoomIn(); else zoomOut() }
  pinchStart = 0; pinchEnd = 0
}

onMounted(async () => {
  try {
    const [cfg, pts] = await Promise.all([
      client.get('/map/basemap-config'),
      client.get('/map/points'),
    ])

    const c = cfg.data
    updateTileUrls(c.tileUrlNormal, c.tileUrlSatellite)

    // Restore saved basemap preference
    const savedBasemap = localStorage.getItem('mapBasemap')
    if (savedBasemap === 'normal' || savedBasemap === 'satellite') {
      basemap.value = savedBasemap
    } else {
      basemap.value = 'normal'
    }

    const points: MapPoint[] = Array.isArray(pts.data) ? pts.data : []
    allPoints = points
    pointCount.value = points.length

    const instance = await initMap()
    if (!instance) return
    mapInst = instance

    // Apply saved/restored basemap
    switchBasemap(basemap.value)

    if (points.length > 0) {
      clusterLayer = buildClusterLayer(points)
      instance.map.add(clusterLayer)

      dotLayer = buildDotLayer(points)
      instance.map.add(dotLayer)

      dotGL = new GraphicsLayer()
      instance.map.add(dotGL)
      dotGL.visible = false

      thumbGL = new GraphicsLayer()
      instance.map.add(thumbGL)
      thumbGL.visible = false

      // Restore saved mode preference
      const savedMode = localStorage.getItem('mapMode')
      if (savedMode === 'point') {
        switchMode('point')
      }

      // Click handler
      instance.view.on('click', (event: any) => {
        const cr = mapContainer.value!.getBoundingClientRect()
        const sp = { x: event.screenPoint.x + cr.left, y: event.screenPoint.y + cr.top }
        instance.view.hitTest(event).then((response: any) => {
          const hit = response.results?.[0]
          if (hit?.type !== 'graphic' && hit?.type !== 'feature') return
          const graphic = hit.graphic

          // Cluster click — no photoId, just aggregateId + cluster_count
          const clusterCount = graphic.attributes?.cluster_count
          if (clusterCount > 0 && graphic.geometry?.latitude != null) {
            fetchClusterPhotos(graphic.geometry.latitude, graphic.geometry.longitude, instance.view.zoom)
            return
          }

          const photoId = graphic.attributes?.photoId
          if (!photoId) return

          if (mode.value === 'cluster') {
            openPhoto(photoId, sp)
          } else {
            // Point mode: both bubbles and dots open the photo
            openPhoto(photoId, sp)
          }
        })
      })

      // Hover highlight
      let cursorOn = false
      const highlightSet = new Set<any>()
      const layerView = await instance.view.whenLayerView(clusterLayer)
      instance.view.on('pointer-move', (event: any) => {
        const el = mapContainer.value
        if (!el) return
        instance.view.hitTest(event).then((response: any) => {
          const over = response.results?.length > 0
          if (over !== cursorOn) {
            cursorOn = over
            el.style.cursor = over ? 'pointer' : ''
          }
          highlightSet.forEach(h => h.remove())
          highlightSet.clear()
          const hit = response.results?.[0]
          if (hit?.type === 'graphic') {
            const handle = layerView.highlight(hit.graphic)
            highlightSet.add(handle)
          }
        })
      })
    }

    const params = new URLSearchParams(window.location.search)
    const qlat = params.get('lat')
    const qlng = params.get('lng')
    if (qlat && qlng) {
      const lat = parseFloat(qlat)
      const lng = parseFloat(qlng)
      if (!isNaN(lat) && !isNaN(lng)) {
        instance.view.goTo({ center: [lng, lat], zoom: 16 }, { duration: 1000 })
      }
    }
  } catch (e) { console.error('MapView init error:', e) }
})

onUnmounted(() => {
  if (extentHandle) extentHandle.remove()
  clearTimeout(extentTimer)
  destroyMap()
  mapInst = null
  clusterLayer = null
  dotLayer = null
  dotGL = null
  thumbGL = null
})
</script>

<template>
  <div class="map-root">
    <div ref="mapContainer" class="map-container"></div>

    <div v-if="loading" class="map-loading">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
    </div>

    <div v-show="!loading && !clusterView" class="map-buttons">
      <button class="map-btn" @click="switchMode(mode === 'cluster' ? 'point' : 'cluster')" :title="mode === 'cluster' ? '显示所有点位' : '聚合显示'">
        <svg v-if="mode === 'cluster'" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="8" cy="8" r="3"/>
          <circle cx="18" cy="10" r="2"/>
          <circle cx="14" cy="19" r="2.5"/>
        </svg>
        <svg v-else viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10"/>
          <circle cx="12" cy="12" r="4" fill="currentColor"/>
        </svg>
      </button>
      <button class="map-btn" @click="toggleBasemap" :title="basemap === 'normal' ? '卫星图' : '普通图'">
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10"/>
          <line x1="2" y1="12" x2="22" y2="12"/>
          <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
        </svg>
      </button>
    </div>

    <div v-show="!loading && !clusterView" class="map-zoom-buttons">
      <button class="map-btn" @click="mapZoomIn" title="放大">
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
      </button>
      <button class="map-btn" @click="mapZoomOut" title="缩小">
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="5" y1="12" x2="19" y2="12"/></svg>
      </button>
    </div>

    <div v-if="clusterView" class="cluster-overlay" @touchstart="onTouchStart" @touchmove="onTouchMove" @touchend="onTouchEnd">
      <div class="cluster-overlay-header">
        <PhotoGridToolbar :count="clusterView.photos.length">
          <template #left>
            <el-button text @click="closeClusterView">
              <el-icon style="margin-right:4px"><ArrowLeft /></el-icon>返回地图
            </el-button>
          </template>
        </PhotoGridToolbar>
      </div>
      <div class="cluster-overlay-body" v-loading="clusterView.loading">
        <template v-for="(group, _gi) in clusterView.groups" :key="_gi">
          <div v-if="group.label" class="cluster-group-header">
            <el-tag type="info" size="large">{{ group.label }}</el-tag>
          </div>
          <div class="cluster-photo-grid" :style="{ gridTemplateColumns: `repeat(${columns}, 1fr)` }">
            <div
              v-for="p in group.photos"
              :key="p.id"
              class="thumb-cell"
              @click="onPhotoClick(p.id, $event)"
            >
              <img v-lazy-img="thumbUrl(p.id, 'grid', 300)" class="thumb-img" />
            </div>
          </div>
        </template>
        <el-empty v-if="!clusterView.loading && clusterView.photos.length === 0" description="该位置没有照片" />
      </div>
    </div>
  </div>
</template>

<style>
.app-main:has(.map-root) { padding: 0; }
.map-root { position: absolute; inset: 0; outline: none; }
.map-container { width: 100%; height: 100%; }
.esri-view .esri-view-surface:focus::after,
.esri-view .esri-view-surface:focus-visible::after {
  outline: none !important;
}
.map-loading {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  background: var(--el-bg-color-page);
  z-index: 20; pointer-events: none;
}
.map-buttons {
  position: absolute;
  bottom: 20px; right: 16px;
  z-index: 10;
  display: flex; flex-direction: column;
  gap: 8px;
}
.map-btn {
  display: flex; align-items: center; justify-content: center;
  width: 40px; height: 40px;
  background: var(--el-bg-color-overlay);
  border: 1px solid var(--el-border-color-light);
  border-radius: 10px;
  box-shadow: 0 2px 8px rgba(0,0,0,.12);
  cursor: pointer;
  color: var(--el-text-color-primary);
  transition: background .2s;
}
.map-btn:hover { background: var(--el-fill-color-light); }
.map-zoom-buttons {
  position: absolute;
  bottom: 20px;
  left: 16px;
  z-index: 10;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.map-zoom-buttons .map-btn { width: 36px; height: 36px; border-radius: 8px; }
.esri-zoom { display: none !important; }
.esri-popup__main-container { max-width: 280px !important; }
.esri-attribution { display: none !important; }
.cluster-overlay {
  position: absolute; inset: 0; z-index: 30;
  display: flex; flex-direction: column;
  background: var(--el-bg-color-page);
  outline: none;
}
.cluster-overlay *:focus { outline: none; }
.cluster-overlay-header {
  flex-shrink: 0;
  display: flex; align-items: center; gap: 12px;
  padding: 8px 16px;
  background: var(--el-bg-color-overlay);
  border-bottom: 1px solid var(--el-border-color-light);
}
.cluster-overlay-body {
  flex: 1; overflow-y: auto;
  padding: 12px 16px;
}
.cluster-group-header { padding: 6px 0 4px 0; }
.cluster-photo-grid { display: grid; gap: 4px; padding-bottom: 8px; }
@media (max-width: 767px) {
  .cluster-overlay-body { padding: 12px 0; }
  .cluster-group-header { padding: 6px 16px 4px; }
}
.thumb-cell {
  aspect-ratio: 1;
  overflow: hidden;
  cursor: pointer;
  background: var(--el-fill-color-light);
}
.thumb-img {
  width: 100%; height: 100%;
  object-fit: cover;
  transition: transform .2s;
}
.thumb-cell:hover .thumb-img { transform: scale(1.05); }
</style>
