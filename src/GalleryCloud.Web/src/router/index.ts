import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { guest: true } },

    {
      path: '/',
      component: () => import('../views/MainLayout.vue'),
      children: [
        { path: '', redirect: '/timeline' },
        { path: 'timeline', name: 'timeline', component: () => import('../views/TimelineView.vue') },
        { path: 'folders', name: 'folders', component: () => import('../views/FileTreeView.vue') },
        { path: 'map', name: 'map', component: () => import('../views/MapView.vue') },
        { path: 'search', name: 'search', component: () => import('../views/SearchView.vue') },
        { path: 'favorites', name: 'favorites', component: () => import('../views/FavoritesView.vue') },
        {
          path: 'manage',
          component: () => import('../views/user/ManageLayout.vue'),
          children: [
            { path: '', redirect: '/manage/dashboard' },
            { path: 'dashboard', name: 'user-dashboard', component: () => import('../views/UserDashboardView.vue') },
            { path: 'scan', name: 'user-scan', component: () => import('../views/UserScanView.vue') },
          ],
        },
        {
          path: 'admin',
          component: () => import('../views/admin/AdminLayout.vue'),
          meta: { requiresAdmin: true },
          children: [
            { path: '', name: 'admin-dashboard', component: () => import('../views/admin/DashboardView.vue') },
            { path: 'users', name: 'admin-users', component: () => import('../views/admin/UsersView.vue') },
            { path: 'settings', name: 'admin-settings', component: () => import('../views/admin/SettingsView.vue') },
          ],
        },
      ],
    },
    { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('../views/NotFoundView.vue') },
  ],
})

router.beforeEach((to, _from) => {
  const auth = useAuthStore()
  // Admin can only access admin routes
  if (auth.isAdmin && !to.path.startsWith('/admin') && to.path !== '/login')
    return '/admin'
  // Non-admin cannot access admin routes
  if (!auth.isAdmin && to.path.startsWith('/admin'))
    return '/timeline'
  if (!auth.isAuthenticated && to.path !== '/login')
    return '/login'
  if (auth.isAuthenticated && to.meta.guest)
    return auth.isAdmin ? '/admin' : '/timeline'
})

export default router
