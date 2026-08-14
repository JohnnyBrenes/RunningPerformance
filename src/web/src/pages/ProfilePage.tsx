import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { ProfileService, type HealthContextResponse, type SaveHealthContextRequest, type UpdateAthleteProfileRequest } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { DataManagementPanel } from '../components/DataManagementPanel'
import { HomeScreenInstallPanel } from '../components/HomeScreenInstallPanel'
import { readableApiError } from '../lib/api'

type ProfileForm = Omit<UpdateAthleteProfileRequest, 'heightCm' | 'weightKg'> & { heightCm: string; weightKg: string }

const healthDefaults: SaveHealthContextRequest = {
  contextType: 'injury_history', bodyLocation: '', startedOn: null, endedOn: null, status: 'active', description: '',
}

function formatDate(value: string | null) {
  return value ? new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`)) : 'Sin fecha'
}

export function ProfilePage() {
  const queryClient = useQueryClient()
  const [editingProfile, setEditingProfile] = useState(false)
  const [editingHealth, setEditingHealth] = useState<HealthContextResponse | 'new' | null>(null)
  const profile = useQuery({ queryKey: ['profile'], queryFn: () => ProfileService.getProfile() })
  const health = useQuery({ queryKey: ['health-contexts'], queryFn: () => ProfileService.getHealthContexts() })
  const profileForm = useForm<ProfileForm>()
  const healthForm = useForm<SaveHealthContextRequest>({ defaultValues: healthDefaults })

  useEffect(() => {
    if (profile.data) profileForm.reset({
      ...profile.data,
      birthDate: profile.data.birthDate,
      heightCm: profile.data.heightCm?.toString() ?? '',
      weightKg: profile.data.weightKg?.toString() ?? '',
    })
  }, [profile.data, profileForm])

  const saveProfile = useMutation({
    mutationFn: (values: ProfileForm) => ProfileService.updateProfile({ requestBody: {
      ...values,
      heightCm: values.heightCm || null,
      weightKg: values.weightKg || null,
      birthDate: values.birthDate || null,
    } }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['profile'] })
      setEditingProfile(false)
    },
  })

  const saveHealth = useMutation({
    mutationFn: (values: SaveHealthContextRequest) => editingHealth === 'new'
      ? ProfileService.createHealthContext({ requestBody: cleanHealth(values) })
      : ProfileService.updateHealthContext({ id: editingHealth!.id, requestBody: cleanHealth(values) }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['health-contexts'] })
      setEditingHealth(null)
    },
  })

  const openHealth = (entry: HealthContextResponse | 'new') => {
    setEditingHealth(entry)
    healthForm.reset(entry === 'new' ? healthDefaults : {
      contextType: entry.contextType,
      bodyLocation: entry.bodyLocation,
      startedOn: entry.startedOn,
      endedOn: entry.endedOn,
      status: entry.status,
      description: entry.description,
    })
  }

  if (profile.isPending || health.isPending) return <LoadingState label="Cargando perfil" />
  if (profile.isError || health.isError) return <ErrorState message={readableApiError(profile.error ?? health.error)} retry={() => void Promise.all([profile.refetch(), health.refetch()])} />

  return (
    <div className="page">
      <header className="page-heading split-heading"><div><p className="eyebrow">Contexto personal</p><h1>Perfil</h1><p>Los datos que dan sentido a cada recomendación.</p></div></header>

      <section className="section-block">
        <div className="section-heading"><div><span className="section-label">Datos básicos</span><h2>Sobre ti</h2></div><button className="button secondary" type="button" onClick={() => setEditingProfile((value) => !value)}>{editingProfile ? 'Cancelar' : 'Editar perfil'}</button></div>
        {editingProfile ? (
          <form className="form-card form-grid" onSubmit={profileForm.handleSubmit((values) => saveProfile.mutate(values))}>
            <label className="span-2">Nombre para mostrar<input {...profileForm.register('displayName', { required: true, maxLength: 120 })} /></label>
            <label>Fecha de nacimiento<input type="date" {...profileForm.register('birthDate')} /></label>
            <label>Sexo para ilustraciones<select {...profileForm.register('sex')}><option value="unspecified">Prefiero no indicar</option><option value="male">Masculino</option><option value="female">Femenino</option></select></label>
            <label>Zona horaria<input {...profileForm.register('timezoneName', { required: true })} /></label>
            <label>Estatura (cm)<input type="number" min="80" max="260" step="0.1" {...profileForm.register('heightCm')} /></label>
            <label>Peso (kg)<input type="number" min="25" max="400" step="0.1" {...profileForm.register('weightKg')} /></label>
            <label>Idioma<input {...profileForm.register('locale', { required: true })} /></label>
            <label>Sistema de unidades<select {...profileForm.register('unitSystem')}><option value="metric">Métrico</option></select></label>
            {saveProfile.isError && <p className="form-alert span-2" role="alert">{readableApiError(saveProfile.error)}</p>}
            <div className="form-actions span-2"><button className="button primary" disabled={saveProfile.isPending}>{saveProfile.isPending ? 'Guardando…' : 'Guardar cambios'}</button></div>
          </form>
        ) : (
          <article className="profile-card">
            <div className="profile-identity"><div className="avatar large">{profile.data.displayName.charAt(0)}</div><div><h2>{profile.data.displayName}</h2><p>Actualizado {new Intl.DateTimeFormat('es', { dateStyle: 'medium' }).format(new Date(profile.data.updatedAt))}</p></div></div>
            <dl className="details-grid"><div><dt>Nacimiento</dt><dd>{formatDate(profile.data.birthDate)}</dd></div><div><dt>Sexo para ilustraciones</dt><dd>{sexLabel(profile.data.sex)}</dd></div><div><dt>Zona horaria</dt><dd>{profile.data.timezoneName}</dd></div><div><dt>Estatura</dt><dd>{profile.data.heightCm ?? '—'} cm</dd></div><div><dt>Peso</dt><dd>{profile.data.weightKg ?? '—'} kg</dd></div><div><dt>Idioma</dt><dd>{profile.data.locale}</dd></div><div><dt>Unidades</dt><dd>{profile.data.unitSystem === 'metric' ? 'Métricas' : 'Imperiales'}</dd></div></dl>
          </article>
        )}
      </section>

      <section className="section-block">
        <div className="section-heading"><div><span className="section-label">Historial relevante</span><h2>Salud y contexto</h2></div><button className="button primary" type="button" onClick={() => openHealth('new')}>Añadir contexto</button></div>
        {editingHealth && (
          <form className="form-card form-grid compact-form" onSubmit={healthForm.handleSubmit((values) => saveHealth.mutate(values))}>
            <label>Tipo<select {...healthForm.register('contextType')}><option value="injury_history">Antecedente de lesión</option><option value="discomfort">Molestia</option><option value="restriction">Restricción</option><option value="other">Otro</option></select></label>
            <label>Estado<select {...healthForm.register('status')}><option value="active">Activo</option><option value="monitoring">En observación</option><option value="resolved">Resuelto</option></select></label>
            <label>Zona corporal<input placeholder="Opcional" {...healthForm.register('bodyLocation')} /></label>
            <label>Inicio<input type="date" {...healthForm.register('startedOn')} /></label>
            <label>Fin<input type="date" {...healthForm.register('endedOn')} /></label>
            <label className="span-2">Descripción<textarea rows={3} {...healthForm.register('description', { required: true, maxLength: 2000 })} /></label>
            {saveHealth.isError && <p className="form-alert span-2">{readableApiError(saveHealth.error)}</p>}
            <div className="form-actions span-2"><button className="button ghost" type="button" onClick={() => setEditingHealth(null)}>Cancelar</button><button className="button primary" disabled={saveHealth.isPending}>Guardar</button></div>
          </form>
        )}
        {health.data.length === 0 ? <EmptyState title="Sin antecedentes registrados">Añade sólo lo que pueda cambiar cómo interpretas el entrenamiento.</EmptyState> : (
          <div className="timeline">
            {health.data.map((entry) => <button className="timeline-item" type="button" key={entry.id} onClick={() => openHealth(entry)}>
              <span className={`status-dot ${entry.status}`} /><span><strong>{entry.description}</strong><small>{entry.bodyLocation || healthLabel(entry.contextType)} · {formatDate(entry.startedOn)}</small></span><span className="tag">{statusLabel(entry.status)}</span>
            </button>)}
          </div>
        )}
      </section>
      <section className="section-block"><HomeScreenInstallPanel /></section>
      <section className="section-block"><DataManagementPanel /></section>
    </div>
  )
}

function cleanHealth(values: SaveHealthContextRequest): SaveHealthContextRequest {
  return { ...values, bodyLocation: values.bodyLocation || null, startedOn: values.startedOn || null, endedOn: values.endedOn || null }
}

function healthLabel(value: string) {
  return ({ injury_history: 'Antecedente de lesión', discomfort: 'Molestia', restriction: 'Restricción', other: 'Otro' } as Record<string, string>)[value] ?? value
}

function statusLabel(value: string) {
  return ({ active: 'Activo', monitoring: 'En observación', resolved: 'Resuelto' } as Record<string, string>)[value] ?? value
}

function sexLabel(value: string) {
  return ({ male: 'Masculino', female: 'Femenino', unspecified: 'Sin especificar' } as Record<string, string>)[value] ?? 'Sin especificar'
}
