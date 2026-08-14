/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlannedSessionResponse } from './PlannedSessionResponse';
import type { TrainingPlanVersionSummaryResponse } from './TrainingPlanVersionSummaryResponse';
export type TrainingPlanDetailResponse = {
    id: string;
    name: string;
    purpose: string;
    planStatus: string;
    version: TrainingPlanVersionSummaryResponse;
    sessions: Array<PlannedSessionResponse>;
};

