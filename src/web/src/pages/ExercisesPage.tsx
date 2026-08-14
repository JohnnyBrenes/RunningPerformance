import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { ExercisesService, ProfileService, type ExerciseResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'
import { selectExerciseMedia } from '../lib/exerciseMedia'

export function ExercisesPage() {
  const [selected, setSelected] = useState<ExerciseResponse | null>(null)
  const profile = useQuery({ queryKey: ['profile'], queryFn: () => ProfileService.getProfile() })
  const exercises = useQuery({ queryKey: ['exercises'], queryFn: () => ExercisesService.getExercises() })

  if (profile.isPending || exercises.isPending) return <LoadingState label="Cargando catálogo" />
  if (profile.isError || exercises.isError) {
    return <ErrorState message={readableApiError(profile.error ?? exercises.error)} retry={() => void Promise.all([profile.refetch(), exercises.refetch()])} />
  }

  return (
    <div className="page">
      <header className="page-heading split-heading">
        <div><p className="eyebrow">Biblioteca técnica</p><h1>Ejercicios</h1><p>La guía escrita siempre está disponible; la imagen se adapta a tu perfil.</p></div>
        <span className="tag">{exercises.data.length} ejercicios</span>
      </header>

      {exercises.data.length === 0 ? <EmptyState title="Catálogo vacío">Los ejercicios aparecerán aquí cuando se añada la primera revisión.</EmptyState> : (
        <section className="exercise-grid" aria-label="Catálogo de ejercicios">
          {exercises.data.map((exercise) => {
            const media = selectExerciseMedia(exercise.revision.media, profile.data.sex)
            return (
              <article className="exercise-card" key={exercise.id}>
                <div className="exercise-visual">
                  {media ? <img src={media.assetUri} alt={media.altText} width={media.widthPx} height={media.heightPx} loading="lazy" /> : <span aria-hidden="true">Técnica<br />sin imagen</span>}
                </div>
                <div className="exercise-copy">
                  <div className="exercise-meta"><span>{movementLabel(exercise.movementPattern)}</span><span>v{exercise.revision.versionNumber}</span></div>
                  <h2>{exercise.revision.displayName}</h2>
                  <p>{exercise.revision.briefDescription}</p>
                  <button className="button secondary" type="button" onClick={() => setSelected(exercise)}>Ver técnica</button>
                </div>
              </article>
            )
          })}
        </section>
      )}

      {selected && <ExerciseGuide exercise={selected} sex={profile.data.sex} close={() => setSelected(null)} />}
    </div>
  )
}

function ExerciseGuide({ exercise, sex, close }: { exercise: ExerciseResponse; sex: string; close: () => void }) {
  const media = selectExerciseMedia(exercise.revision.media, sex)
  return (
    <div className="drawer-backdrop" role="presentation" onMouseDown={close}>
      <section className="history-drawer exercise-drawer" role="dialog" aria-modal="true" aria-labelledby="exercise-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="form-title"><div><span className="section-label">Guía v{exercise.revision.versionNumber}</span><h2 id="exercise-title">{exercise.revision.displayName}</h2><p>{exercise.equipment || 'Sin equipo'}</p></div><button className="icon-button" type="button" aria-label="Cerrar" onClick={close}>×</button></div>
        {media && <figure className="guide-figure"><img src={media.assetUri} alt={media.altText} width={media.widthPx} height={media.heightPx} /><figcaption>{media.source} · {media.license}</figcaption></figure>}
        <div className="instruction-grid">
          <section><span className="step-number">01</span><div><h3>Preparación</h3><p>{exercise.revision.setup}</p></div></section>
          <section><span className="step-number">02</span><div><h3>Ejecución</h3><p>{exercise.revision.execution}</p></div></section>
          <section className="safety-step"><span className="step-number">!</span><div><h3>Puntos de seguridad</h3><p>{exercise.revision.safetyCues}</p></div></section>
        </div>
      </section>
    </div>
  )
}

function movementLabel(value: string | null) {
  return ({
    squat: 'Sentadilla',
    hinge: 'Bisagra',
    unilateral: 'Unilateral',
    pull: 'Tirón',
    push: 'Empuje',
    core: 'Tronco',
    knee_extension: 'Rodilla',
    plyometric: 'Pliometría',
  } as Record<string, string>)[value ?? ''] ?? value ?? 'General'
}
