<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import client from '../api/client'
import { thumbUrl } from '../composables/useThumbnailUrl'
import { usePhotoGrid } from '../composables/usePhotoGrid'
import { usePhotoViewStore } from '../stores/photoViewStore'
import Map from '@arcgis/core/Map'
import MapView from '@arcgis/core/views/MapView'
import Basemap from '@arcgis/core/Basemap'
import WebTileLayer from '@arcgis/core/layers/WebTileLayer'
import FeatureLayer from '@arcgis/core/layers/FeatureLayer'
import Graphic from '@arcgis/core/Graphic'
import Point from '@arcgis/core/geometry/Point'
import PopupTemplate from '@arcgis/core/PopupTemplate'
import SimpleRenderer from '@arcgis/core/renderers/SimpleRenderer'
import SimpleMarkerSymbol from '@arcgis/core/symbols/SimpleMarkerSymbol'
import PhotoGridToolbar from '../components/PhotoGridToolbar.vue'

interface MapPoint { id: string; latitude: number; longitude: number; fileName: string; takenAt: string | null }

const viewStore = usePhotoViewStore()
const { columns, zoomIn, zoomOut } = usePhotoGrid()
const mapContainer = ref<HTMLDivElement | null>(null)
const loading = ref(true)
const pointCount = ref(0)
const basemap = ref<'normal' | 'satellite'>('normal')
let allPoints: MapPoint[] = []
const clusterView = ref<{ lat: number; lng: number; photos: any[]; groups: any[]; loading: boolean } | null>(null)

let tileUrlNormal = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
let tileUrlSatellite = 'https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}'
let map: any = null
let mapView: any = null

function escapeHtml(s: string) {
  const d = document.createElement('div')
  d.textContent = s
  return d.innerHTML
}

function makePopupContent(feature: any): HTMLDivElement {
  const attrs = feature.graphic.attributes
  const thumbSrc = thumbUrl(attrs.photoId, 'grid', 240)
  const date = attrs.takenAt ? attrs.takenAt.substring(0, 10) : '未知'
  const gps = `${Number(attrs.latitude).toFixed(4)}, ${Number(attrs.longitude).toFixed(4)}`

  const div = document.createElement('div')
  div.className = 'map-popup'
  div.innerHTML = `
    <img src="${thumbSrc}" style="width:240px;max-height:180px;object-fit:cover;border-radius:4px;display:block"
         loading="lazy"
         onerror="this.remove()" />
    <div style="font-size:13px;font-weight:500;margin:6px 0 2px">${escapeHtml(attrs.fileName)}</div>
    <div style="font-size:12px;color:var(--el-text-color-secondary)">${date} · ${gps}</div>
    <button class="el-button el-button--primary el-button--small" style="margin-top:8px">查看完整照片</button>
  `
  div.querySelector('button')?.addEventListener('click', () => {
    viewStore.show(attrs.photoId, { x: 0, y: 0, width: 0, height: 0 }, '')
  })
  return div
}

function buildLayer(points: MapPoint[]) {
  const graphics = points.map((p, i) => new Graphic({
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

  return new FeatureLayer({
    source: graphics,
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
      popupEnabled: false,
    } as any,
    popupTemplate: new PopupTemplate({
      title: '{fileName}',
      content: makePopupContent,
      outFields: ['photoId', 'fileName', 'takenAt', 'latitude', 'longitude'],
    }),
  })
}

function switchBasemap(type: 'normal' | 'satellite') {
  if (!map) return
  basemap.value = type
  const url = type === 'normal' ? tileUrlNormal : tileUrlSatellite
  map.basemap = new Basemap({
    baseLayers: [new WebTileLayer({ urlTemplate: url })]
  })
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
  const groups: { label: string; photos: any[] }[] = []
  let lastLabel = ''
  for (const p of photos) {
    const label = p.takenAt ? p.takenAt.substring(0, 7) : '未知日期'
    if (label !== lastLabel) {
      groups.push({ label, photos: [p] })
      lastLabel = label
    } else {
      groups[groups.length - 1].photos.push(p)
    }
  }
  return groups
}

function fetchClusterPhotos(lat: number, lng: number, zoom: number) {
  const r2 = getRadiusFromZoom(zoom) ** 2
  clusterView.value = { lat, lng, photos: [], groups: [], loading: true }
  // Client-side filtering — instant, no network request
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
  viewStore.show(photoId, { x: r.x, y: r.y, width: r.width, height: r.height }, img?.src)
}

function closeClusterView() {
  clusterView.value = null
}

// Pinch zoom for cluster photo grid
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
    if (c.tileUrlNormal) tileUrlNormal = c.tileUrlNormal
    if (c.tileUrlSatellite) tileUrlSatellite = c.tileUrlSatellite
    basemap.value = c.defaultBasemap === 'satellite' ? 'satellite' : 'normal'

    const points: MapPoint[] = Array.isArray(pts.data) ? pts.data : []
    allPoints = points
    pointCount.value = points.length

    const tileUrl = basemap.value === 'normal' ? tileUrlNormal : tileUrlSatellite
    map = new Map({
      basemap: new Basemap({
        baseLayers: [new WebTileLayer({ urlTemplate: tileUrl })]
      })
    })

    mapView = new MapView({
      container: mapContainer.value!,
      map,
      center: [105, 35],
      zoom: 4,
    })

    await mapView.when()

    if (points.length > 0) {
      const layer = buildLayer(points)
      map.add(layer)

      // Handle cluster click
      mapView.on('click', (event: any) => {
        mapView.hitTest(event).then((response: any) => {
          const hit = response.results?.[0]
          if (hit?.type === 'graphic') {
            const graphic = hit.graphic
            // Check if it's a cluster (aggregate feature)
            const clusterCount = graphic.attributes?.cluster_count
            if (clusterCount > 0 && graphic.geometry?.latitude != null) {
              event.stopPropagation()
              fetchClusterPhotos(graphic.geometry.latitude, graphic.geometry.longitude, mapView.zoom)
            }
          }
        })
      })

      // Pointer cursor + hover highlight on features
      let cursorOn = false, highlightHandle: any = null
      const layerView = await mapView.whenLayerView(layer)
      layerView.highlightOptions = {
        color: [255, 193, 7],
        fillOpacity: 0.25,
        haloOpacity: 0.5,
      }
      mapView.on('pointer-move', (event: any) => {
        const el = mapContainer.value
        if (!el) return
        mapView.hitTest(event).then((response: any) => {
          const over = response.results?.length > 0
          if (over !== cursorOn) {
            cursorOn = over
            el.style.cursor = over ? 'pointer' : ''
          }
          if (highlightHandle) { highlightHandle.remove(); highlightHandle = null }
          const hit = response.results?.[0]
          if (hit?.type === 'graphic') {
            highlightHandle = layerView.highlight(hit.graphic)
          }
        })
      })
    }
  } catch { /* */ }
  finally { loading.value = false }
})

onUnmounted(() => {
  mapView?.destroy()
  map = null
  mapView = null
})
</script>

<template>
  <div class="map-root">
    <!-- Map container -->
    <div ref="mapContainer" class="map-container"></div>

    <!-- Loading overlay -->
    <div v-if="loading" class="map-loading">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
    </div>

    <!-- Floating toolbar -->
    <div v-show="!loading && !clusterView" class="map-toolbar">
      <span class="map-toolbar-info">{{ pointCount }} 个位置点</span>
      <div style="flex:1" />
      <el-radio-group v-model="basemap" size="small" @change="switchBasemap">
        <el-radio-button value="normal">普通</el-radio-button>
        <el-radio-button value="satellite">卫星</el-radio-button>
      </el-radio-group>
    </div>

    <!-- Cluster photo list overlay -->
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
          <div class="cluster-group-header">
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
/* Map fills parent, remove el-main padding */
.app-main:has(.map-root) { padding: 0; }
.map-root { position: absolute; inset: 0; outline: none; }
.map-container { width: 100%; height: 100%; }
/* Kill ArcGIS blue focus outline */
.esri-view .esri-view-surface:focus::after,
.esri-view .esri-view-surface:focus-visible::after {
  outline: none !important;
}

/* Map container */
.map-loading {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  background: var(--el-bg-color-page);
  z-index: 20; pointer-events: none;
}

/* Floating toolbar */
.map-toolbar {
  position: absolute;
  bottom: 20px; left: 16px; right: 16px;
  z-index: 10;
  display: flex; align-items: center; gap: 8px;
  padding: 6px 12px;
  background: var(--el-bg-color-overlay);
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0,0,0,.12);
  pointer-events: auto;
}
.map-toolbar-info { font-size: 13px; color: var(--el-text-color-secondary); }

/* Popup styling */
.map-popup button { cursor: pointer; }

/* Override ArcGIS popup max-width for photo popups */
.esri-popup__main-container { max-width: 280px !important; }

/* Hide ArcGIS attribution */
.esri-attribution { display: none !important; }


/* Cluster photo list overlay */
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
.cluster-group-header {
  padding: 6px 0 4px 0;
}
.cluster-photo-grid {
  display: grid;
  gap: 4px;
  padding-bottom: 8px;
}

/* Thumb cell styles (shared with TimelineView) */
.thumb-cell {
  aspect-ratio: 1;
  overflow: hidden;
  border-radius: 4px;
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
