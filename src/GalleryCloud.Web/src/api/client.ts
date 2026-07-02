import axios from 'axios'
import type { AxiosInstance } from 'axios'

export const API_BASE = import.meta.env.BASE_URL + 'api'

const client: AxiosInstance = axios.create({
  baseURL: API_BASE,
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
})

client.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

let redirecting = false

client.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && !redirecting) {
      redirecting = true
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      window.location.href = '/login'
    }
    return Promise.reject(error)
  }
)

export default client
