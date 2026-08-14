/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ExerciseMediaResponse } from './ExerciseMediaResponse';
export type ExerciseRevisionResponse = {
    id: string;
    versionNumber: number | string;
    displayName: string;
    briefDescription: string;
    setup: string;
    execution: string;
    safetyCues: string;
    media: Array<ExerciseMediaResponse>;
};

