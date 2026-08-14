/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ExerciseRevisionResponse } from './ExerciseRevisionResponse';
export type ExerciseResponse = {
    id: string;
    slug: string;
    canonicalName: string;
    movementPattern: string | null;
    equipment: string | null;
    status: string;
    revision: ExerciseRevisionResponse;
};

