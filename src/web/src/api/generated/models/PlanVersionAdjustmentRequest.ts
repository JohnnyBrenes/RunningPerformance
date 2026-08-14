/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlannedSessionAdjustmentRequest } from './PlannedSessionAdjustmentRequest';
export type PlanVersionAdjustmentRequest = {
    sourcePlanVersionId: string;
    rationale: string;
    reviewCriterion: string;
    sessionChanges: Array<PlannedSessionAdjustmentRequest>;
};

