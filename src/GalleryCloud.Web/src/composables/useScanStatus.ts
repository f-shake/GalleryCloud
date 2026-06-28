import { ref } from 'vue'
import client from '../api/client'

const isScanning = ref(false)
let started = false

export function useScanStatus() {
  if (!started) {
    started = true
    poll()
    setInterval(poll, 5000)
  }
  return { isScanning }
}

async function poll() {
  try { const r = await client.get('/admin/scan/status'); isScanning.value = r.data.isRunning }
  catch { /* */ }
}
