/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TrainingPlanVersionSummaryResponse } from './TrainingPlanVersionSummaryResponse';
export type TrainingPlanSummaryResponse = {
    id: string;
    name: string;
    purpose: string;
    targetStart: string | null;
    targetEnd: string | null;
    status: string;
    versions: Array<TrainingPlanVersionSummaryResponse>;
};

