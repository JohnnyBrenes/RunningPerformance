/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlannedExerciseResponse } from './PlannedExerciseResponse';
export type PlannedSessionBlockResponse = {
    id: string;
    position: number | string;
    blockType: string;
    repeatCount: number | string;
    instructions: string;
    exercises: Array<PlannedExerciseResponse>;
};

