<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
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
const { loading, initMap, destroy, mapInstance } = useMap(mapContainer)
let markerLayer: GraphicsLayer | null = null
let marker: Graphic | null = null

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
  const inst = mapInstance
  if (!inst) return

  // Remove old marker
  if (markerLayer && marker) {
    markerLayer.remove(marker)
  }

  // Create and add new marker
  marker = buildMarker(lat, lng)
  if (!markerLayer) {
    markerLayer = new GraphicsLayer()
    inst.map.add(markerLayer)
  }
  markerLayer.add(marker)

  // Animate to new location
  inst.view.goTo({ center: [lng, lat], zoom: 16 }, { duration: 300 })
}

onMounted(async () => {
  const lat = props.latitude
  const lng = props.longitude
  if (lat == null || lng == null) return

  const inst = await initMap([lng, lat], 16)
  if (!inst) return

  inst.view.ui.move('zoom', 'bottom-right')

  markerLayer = new GraphicsLayer()
  marker = buildMarker(lat, lng)
  markerLayer.add(marker)
  inst.map.add(markerLayer)
})

watch(() => [props.latitude, props.longitude], ([lat, lng]) => {
  if (lat != null && lng != null) {
    updatePosition(lat, lng)
  }
})

onUnmounted(() => destroy())
</script>

<template>
  <div ref="mapContainer" class="map-embed" :class="{ 'map-embed--loading': loading }">
    <div v-if="latitude == null || longitude == null" class="map-embed-empty">
      <el-icon :size="32" color="var(--el-text-color-placeholder)"><MapLocation /></el-icon>
      <span>无空间信息</span>
    </div>
  </div>
</template>

<style scoped>
.map-embed {
  width: 100%;
  height: 100%;
  min-height: 120px;
  border-radius: 8px;
  overflow: hidden;
  position: relative;
}
.map-embed--loading {
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--el-fill-color-light);
}
.map-embed-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  height: 100%;
  width: 100%;
}
</style>
