import { ref, type Ref } from 'vue'
import '@arcgis/core/assets/esri/themes/light/main.css'
import esriConfig from '@arcgis/core/config'
import Map from '@arcgis/core/Map'
import ArcMapView from '@arcgis/core/views/MapView'
import Basemap from '@arcgis/core/Basemap'
import WebTileLayer from '@arcgis/core/layers/WebTileLayer'

export interface MapInstance {
  map: Map
  view: ArcMapView
}

// 确保 assets 路径在 ArcGIS 模块加载前设置
esriConfig.assetsPath = import.meta.env.BASE_URL + 'esri-assets'

export function useMap(containerRef: Ref<HTMLDivElement | null>) {
  const loading = ref(true)
  let tileUrlNormal = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
  let tileUrlSatellite = 'https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}'
  let mapInstance: MapInstance | null = null

  async function initMap(
    center: [number, number] = [105, 35],
    zoom: number = 4,
    initialBasemap: 'normal' | 'satellite' = 'normal',
  ): Promise<MapInstance | null> {
    if (!containerRef.value) return null

    const url = initialBasemap === 'satellite' ? tileUrlSatellite : tileUrlNormal
    const basemap = new Basemap({
      baseLayers: [new WebTileLayer({ urlTemplate: url })]
    })

    const map = new Map({ basemap })

    const view = new ArcMapView({
      container: containerRef.value,
      map,
      center,
      zoom,
    })

    await view.when()
    mapInstance = { map, view }
    loading.value = false
    return mapInstance
  }

  function switchBasemap(type: 'normal' | 'satellite') {
    if (!mapInstance) return
    const url = type === 'normal' ? tileUrlNormal : tileUrlSatellite
    mapInstance.map.basemap = new Basemap({
      baseLayers: [new WebTileLayer({ urlTemplate: url })]
    })
  }

  function updateTileUrls(normal?: string, satellite?: string) {
    if (normal) tileUrlNormal = normal
    if (satellite) tileUrlSatellite = satellite
    return 'normal' as const
  }

  function destroy() {
    mapInstance?.view?.destroy()
    mapInstance = null
  }

  return { loading, initMap, switchBasemap, updateTileUrls, destroy, get mapInstance() { return mapInstance } }
}
