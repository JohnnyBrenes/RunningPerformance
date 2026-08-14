/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ExerciseResponse } from './ExerciseResponse';
export type PlannedExerciseResponse = {
    id: string;
    position: number | string;
    sets: number | string | null;
    repetitionsMin: number | string | null;
    repetitionsMax: number | string | null;
    durationSeconds: number | string | null;
    restSeconds: number | string | null;
    loadValue: number | string | null;
    loadUnit: string | null;
    targetRpe: number | string | null;
    targetRir: number | string | null;
    tempo: string | null;
    side: string | null;
    note: string | null;
    exercise: ExerciseResponse;
};

