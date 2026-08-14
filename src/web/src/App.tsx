import { BrowserRouter, Navigate, Outlet, Route, Routes, useLocation } from 'react-router'
import { lazy, Suspense } from 'react'
import { useAuth } from './auth/AuthProvider'
import { AppShell } from './components/AppShell'
import { LoadingState } from './components/States'

const DashboardPage = lazy(() => import('./pages/DashboardPage').then((module) => ({ default: module.DashboardPage })))
const LoginPage = lazy(() => import('./pages/AuthPages').then((module) => ({ default: module.LoginPage })))
const RecoveryPage = lazy(() => import('./pages/AuthPages').then((module) => ({ default: module.RecoveryPage })))
const ProfilePage = lazy(() => import('./pages/ProfilePage').then((module) => ({ default: module.ProfilePage })))
const RacesPage = lazy(() => import('./pages/RacesPage').then((module) => ({ default: module.RacesPage })))
const CalendarPage = lazy(() => import('./pages/CalendarPage').then((module) => ({ default: module.CalendarPage })))
const PlanPage = lazy(() => import('./pages/PlanPage').then((module) => ({ default: module.PlanPage })))
const ExercisesPage = lazy(() => import('./pages/ExercisesPage').then((module) => ({ default: module.ExercisesPage })))
const EvaluationsPage = lazy(() => import('./pages/EvaluationsPage').then((module) => ({ default: module.EvaluationsPage })))
const ActivitiesPage = lazy(() => import('./pages/ActivitiesPage').then((module) => ({ default: module.ActivitiesPage })))

function ProtectedRoute() {
  const { ready, session } = useAuth()
  const location = useLocation()

  if (!ready) return <main className="bootstrap-state"><LoadingState label="Comprobando tu sesión" /></main>
  if (!session) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}

export function App() {
  return (
    <BrowserRouter>
      <Suspense fallback={<main className="bootstrap-state"><LoadingState /></main>}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/recover" element={<RecoveryPage />} />
          <Route element={<ProtectedRoute />}>
            <Route element={<AppShell />}>
              <Route index element={<DashboardPage />} />
              <Route path="profile" element={<ProfilePage />} />
              <Route path="races" element={<RacesPage />} />
              <Route path="calendar" element={<CalendarPage />} />
              <Route path="plan" element={<PlanPage />} />
              <Route path="evaluations" element={<EvaluationsPage />} />
              <Route path="activities" element={<ActivitiesPage />} />
              <Route path="exercises" element={<ExercisesPage />} />
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  )
}
