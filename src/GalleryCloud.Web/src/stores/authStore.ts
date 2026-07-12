import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { User } from '../types'
import client from '../api/client'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem('token'))
  const user = ref<User | null>(JSON.parse(localStorage.getItem('user') || 'null'))

  const isAuthenticated = computed(() => !!token.value)
  const isAdmin = computed(() => user.value?.id === 'admin')

  // Sync existing token to cookie for <img> tag auth
  if (token.value) {
    document.cookie = `token=${token.value};path=/;SameSite=Lax`
  }

  function setTokenCookie(t: string) {
    document.cookie = `token=${t};path=/;SameSite=Lax`
  }

  function clearTokenCookie() {
    document.cookie = 'token=;path=/;SameSite=Lax;max-age=0'
  }

  async function login(username: string, password: string): Promise<boolean> {
    try {
      const res = await client.post('/auth/login', { username, password })
      token.value = res.data.token
      user.value = res.data.user
      localStorage.setItem('token', res.data.token)
      localStorage.setItem('user', JSON.stringify(res.data.user))
      setTokenCookie(res.data.token)
      return true
    } catch {
      return false
    }
  }

  function logout() {
    token.value = null
    user.value = null
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    clearTokenCookie()
  }

  return { token, user, isAuthenticated, isAdmin, login, logout }
})
